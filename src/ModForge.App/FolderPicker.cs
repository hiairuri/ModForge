using System.Runtime.InteropServices;

namespace ModForge.App;

/// <summary>
/// 系统原生的文件 / 文件夹选择对话框，直接走 Windows Shell 的 IFileOpenDialog。
///
/// 为什么必须由编辑器进程弹：编辑器是个本地网页，浏览器的 &lt;input type="file"&gt; 只能给出
/// 文件内容，**拿不到真实路径**（浏览器出于安全刻意不给）；而"打开工程"需要的恰恰是一个
/// 目录路径。所以这件事只能在服务端进程里做。
///
/// 为什么直接用 Shell 的 COM 接口，而不是 WinForms 的 FolderBrowserDialog：
///
///   WinForms 那个控件要挂在一个宿主窗口上才肯显示。控制台程序没有窗口，于是得先造一个
///   隐形的 Form 当宿主——而"隐形 + 置顶 + 最小化"的宿主窗口会被 Windows 反复抬升，
///   表现为对话框一直在闪（实测）。这里直接调 Shell 的对话框，不经过 WinForms，
///   没有宿主窗口，也就没有可闪的东西；而且弹出的就是资源管理器同款的原生对话框。
///
/// 对话框的清晰度由进程的 DPI 感知决定（见 <see cref="EnablePerMonitorDpi"/>）：
/// 进程若声明为 DPI 不感知，Windows 会把整个对话框当低分辨率位图放大，文字发虚。
/// </summary>
internal static class FolderPicker
{
    public sealed record PickResult(bool Ok, string? Path, string? Error);

    /// <summary>用户在对话框里点了取消。这不是错误。</summary>
    private const int HResultCancelled = unchecked((int)0x800704C7);   // HRESULT_FROM_WIN32(ERROR_CANCELLED)

    // FILEOPENDIALOGOPTIONS
    private const uint FosPickFolders = 0x00000020;
    private const uint FosForceFileSystem = 0x00000040;
    private const uint FosPathMustExist = 0x00000800;

    private const uint SigdnFileSysPath = 0x80058000;
    private const uint ClsctxAll = 0x17;
    private const uint CoinitApartmentThreaded = 0x2;

    private static readonly Guid ClsidFileOpenDialog = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
    private static readonly Guid IidFileOpenDialog = new("D57C7288-D4AD-4768-BE02-9D969532D960");
    private static readonly Guid IidShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    /// <summary>对话框最多开多久。防呆，避免请求永远挂着。</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    /* ---------------------------------------------------------------- DPI */

    private static readonly IntPtr DpiAwarenessPerMonitorV2 = new(-4);
    private static readonly IntPtr DpiAwarenessPerMonitor = new(-3);
    private static readonly IntPtr DpiAwarenessSystem = new(-2);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("shcore.dll", SetLastError = true)]
    private static extern int SetProcessDpiAwareness(int value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDPIAware();

    /// <summary>当前前台窗口。用作对话框的 owner，让对话框出现在用户正在看的那个窗口之上。</summary>
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    /// <summary>
    /// 声明进程 DPI 感知。必须在创建任何窗口之前调用（所以放在 Main 最前面）。
    ///
    /// 不声明的话，在 250% 缩放的显示器上，Windows 会把我们的对话框当作低分辨率位图
    /// 整体放大 2.5 倍——文字明显比浏览器自己的弹窗模糊，这正是用户报告的问题。
    ///
    /// 按新到旧的顺序尝试；都被拒（例如已被清单设定）就保持现状，不影响功能。
    /// </summary>
    public static void EnablePerMonitorDpi()
    {
        try
        {
            if (SetProcessDpiAwarenessContext(DpiAwarenessPerMonitorV2) ||
                SetProcessDpiAwarenessContext(DpiAwarenessPerMonitor) ||
                SetProcessDpiAwarenessContext(DpiAwarenessSystem))
            {
                return;
            }
        }
        catch (EntryPointNotFoundException)
        {
            // 老系统没有这个入口，继续往下退。
        }

        try
        {
            // Windows 8.1+：PROCESS_PER_MONITOR_DPI_AWARE = 2
            if (SetProcessDpiAwareness(2) == 0)
            {
                return;
            }
        }
        catch (EntryPointNotFoundException)
        {
            // 继续退。
        }

        try
        {
            SetProcessDPIAware();
        }
        catch (EntryPointNotFoundException)
        {
            // 都不可用就算了。
        }
    }

    /* ---------------------------------------------------------------- 对外接口 */

    /// <summary>
    /// 选一个目录。取消返回 Ok=true 且 Path=null。
    ///
    /// <paramref name="title"/> 显示在对话框标题栏，用来告诉用户"正在选哪个路径"
    /// （调用方传的是设置页里那个字段的标签）。
    /// </summary>
    public static PickResult PickFolder(string? startAt, string? title = null) =>
        Run(startAt, title, pickFolders: true);

    /// <summary>
    /// 选一个图片文件。取消返回 Ok=true 且 Path=null。
    ///
    /// 目前只有一处用得上：工程的 mod 封面（<c>About/Preview.png</c>）。与选目录共用同一套
    /// 线程、owner、错误处理，区别只是不勾 FOS_PICKFOLDERS 并加上图片类型的过滤。
    /// </summary>
    public static PickResult PickImage(string? startAt, string? title = null) =>
        Run(startAt, title, pickFolders: false);

    /* ---------------------------------------------------------------- 实现 */

    private static PickResult Run(string? startAt, string? title, bool pickFolders)
    {
        string? picked = null;
        string? failure = null;

        // Shell 的对话框要求 STA 单元，而请求处理线程是 MTA。另起一条 STA 线程跑它再汇合。
        var thread = new Thread(() =>
        {
            try
            {
                picked = ShowOnThisThread(startAt, title, pickFolders);
            }
            catch (Exception ex)
            {
                failure = ex.Message;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        if (!thread.Join(Timeout))
        {
            return new PickResult(false, null, "等待选择对话框超时。");
        }

        if (failure is not null)
        {
            return new PickResult(false, null, failure);
        }

        // 取消：Ok=true 但没有路径，界面据此什么都不做（取消不该弹错误）。
        return new PickResult(true, picked, null);
    }

    /// <summary>
    /// 在本线程（必须是 STA）显示对话框，返回选中的路径；取消返回 null。
    ///
    /// 两种模式：选目录（工程文件夹、素材库、导出目录、游戏目录）与选图片文件（mod 封面）。
    /// </summary>
    private static string? ShowOnThisThread(string? startAt, string? title, bool pickFolders)
    {
        int hr = CoInitializeEx(IntPtr.Zero, CoinitApartmentThreaded);

        // S_OK(0) / S_FALSE(1) 都表示这次调用要配一次 CoUninitialize；
        // RPC_E_CHANGED_MODE 表示这个线程已经初始化过，那就不用我们收尾。
        bool ownsCom = hr is 0 or 1;

        // 图片过滤器的三个指针：两个字符串必须活到对话框关掉之后（系统按指针读它们），
        // 所以要一直留到最外层收尾时再释放。声明放在 try 之外，收尾那一层才看得见。
        IntPtr filterName = IntPtr.Zero;
        IntPtr filterSpec = IntPtr.Zero;
        IntPtr filterBlock = IntPtr.Zero;

        try
        {
            Guid clsid = ClsidFileOpenDialog;
            Guid iid = IidFileOpenDialog;

            hr = CoCreateInstance(ref clsid, IntPtr.Zero, ClsctxAll, ref iid, out IFileOpenDialog? dialog);

            if (hr != 0 || dialog is null)
            {
                throw new InvalidOperationException($"无法创建系统文件对话框（HRESULT 0x{hr:X8}）。");
            }

            try
            {
                // 合并调用方给的选项，不要覆盖掉类默认值。
                dialog.GetOptions(out uint current);
                uint wanted = current | FosForceFileSystem | FosPathMustExist;

                if (pickFolders)
                {
                    wanted |= FosPickFolders;
                }

                dialog.SetOptions(wanted);

                if (!pickFolders)
                {
                    // 选文件时给一个图片类型的过滤：编辑器要的就是图，别的选了也用不上。
                    filterName = Marshal.StringToHGlobalUni("图片");
                    filterSpec = Marshal.StringToHGlobalUni("*.png;*.jpg;*.jpeg;*.webp;*.bmp");
                    filterBlock = Marshal.AllocHGlobal(IntPtr.Size * 2);
                    Marshal.WriteIntPtr(filterBlock, 0, filterName);
                    Marshal.WriteIntPtr(filterBlock, IntPtr.Size, filterSpec);

                    dialog.SetFileTypes(1, filterBlock);
                }

                // 标题用"这个路径是干什么的"（调用方给的是字段标签），标题栏放得下；
                // 不设 OK 按钮文字——那个位置窄，长标签会被截断，默认的"选择文件夹"更清楚。
                if (!string.IsNullOrWhiteSpace(title))
                {
                    dialog.SetTitle(title);
                }

                if (!string.IsNullOrWhiteSpace(startAt))
                {
                    SetStartFolder(dialog, startAt);
                }

                // 把对话框挂在"当前前台窗口"上（owner）。
                //
                // 不传 owner 的话，对话框确实会弹出来，但落在用户当前窗口**后面**——
                // 编辑器是个后台服务进程，Windows 不给它抢前台的权限，于是用户点了
                // "浏览…"看着像什么都没发生（实测）。给了 owner 之后，系统会把对话框
                // 居中在浏览器窗口上，并按所属窗口的激活状态一起带到前面。
                IntPtr owner = GetForegroundWindow();

                hr = dialog.Show(owner);

                if (hr == HResultCancelled)
                {
                    return null;
                }

                if (hr != 0)
                {
                    throw new InvalidOperationException($"选择对话框返回错误（HRESULT 0x{hr:X8}）。");
                }

                dialog.GetResult(out IShellItem? item);

                if (item is null)
                {
                    return null;
                }

                try
                {
                    return ReadPath(item);
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }
        }
        finally
        {
            if (filterBlock != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(filterBlock);
            }

            if (filterSpec != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(filterSpec);
            }

            if (filterName != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(filterName);
            }

            if (ownsCom)
            {
                CoUninitialize();
            }
        }
    }

    /// <summary>把起始目录设成 Shell 项；路径无效就忽略（对话框回到默认位置）。</summary>
    private static void SetStartFolder(IFileOpenDialog dialog, string startAt)
    {
        string? directory = Directory.Exists(startAt)
            ? startAt
            : File.Exists(startAt) ? Path.GetDirectoryName(startAt) : null;

        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Guid iid = IidShellItem;

        if (SHCreateItemFromParsingName(directory, IntPtr.Zero, ref iid, out IShellItem? folder) == 0 &&
            folder is not null)
        {
            try
            {
                dialog.SetDefaultFolder(folder);
                dialog.SetFolder(folder);
            }
            finally
            {
                Marshal.ReleaseComObject(folder);
            }
        }
    }

    /// <summary>取 Shell 项的完整文件系统路径。</summary>
    private static string ReadPath(IShellItem item)
    {
        item.GetDisplayName(SigdnFileSysPath, out IntPtr buffer);

        try
        {
            return Marshal.PtrToStringUni(buffer) ?? string.Empty;
        }
        finally
        {
            // 这个字符串是 Shell 用 CoTaskMemAlloc 分配的，得由我们释放。
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(buffer);
            }
        }
    }

    /* ---------------------------------------------------------------- 自检 */

    /// <summary>
    /// 自检：建出对话框、设选项、设标题、设过滤、设起始目录，再全部读回来核对。
    ///
    /// **不调 Show()**，所以不需要人点、也不会弹窗。这样能在无人工的情况下验证 COM 接口
    /// 声明是对的——COM 按 vtable 槽位调用，少声明一个方法或顺序写错，调到别的函数上去
    /// 通常表现为设进去的值读出来不对或直接崩。这些断言正好能抓住那类错误。
    ///
    /// 返回进程退出码。
    /// </summary>
    public static int SelfTest()
    {
        Console.WriteLine("选择对话框接口自检（不弹窗，只验证 COM 声明）");
        Console.WriteLine();

        var failures = new List<string>();

        var thread = new Thread(() =>
        {
            int hr = CoInitializeEx(IntPtr.Zero, CoinitApartmentThreaded);
            bool ownsCom = hr is 0 or 1;

            try
            {
                Guid clsid = ClsidFileOpenDialog;
                Guid iid = IidFileOpenDialog;

                hr = CoCreateInstance(ref clsid, IntPtr.Zero, ClsctxAll, ref iid, out IFileOpenDialog? dialog);

                Check(hr == 0 && dialog is not null, $"创建对话框（HRESULT 0x{hr:X8}）");

                if (dialog is null)
                {
                    return;
                }

                try
                {
                    // 1. 设选项再读回来。GetOptions/SetOptions 在 vtable 里相邻，
                    //    槽位错开的话这里立刻能看出来。
                    dialog.GetOptions(out uint original);
                    dialog.SetOptions(original | FosPickFolders | FosForceFileSystem | FosPathMustExist);

                    dialog.GetOptions(out uint updated);

                    Check((updated & FosPickFolders) != 0, "SetOptions/GetOptions 往返（FOS_PICKFOLDERS 生效）");
                    Check((updated & FosPathMustExist) != 0, "选项位没有被写错位置（FOS_PATHMUSTEXIST 生效）");

                    // 2. 标题与按钮文字都是字符串参数；槽位不对通常会崩或写错地方。
                    dialog.SetTitle("ModForge self-test");
                    dialog.SetOkButtonLabel("选择");
                    Check(true, "SetTitle / SetOkButtonLabel 可调用（字符串编组）");

                    // 2b. 文件类型过滤：只有"选图片"那条路用得上，但它同样是一处
                    //     "声明写错就调到别的槽位上"的接口，所以自检也要走到。
                    //     参数是一块 COMDLG_FILTERSPEC 数组（每项两个字符串指针），
                    //     槽位不对在这里就会崩或返回失败。
                    IntPtr filterName = Marshal.StringToHGlobalUni("图片");
                    IntPtr filterSpec = Marshal.StringToHGlobalUni("*.png;*.jpg");

                    try
                    {
                        IntPtr block = Marshal.AllocHGlobal(IntPtr.Size * 2);
                        Marshal.WriteIntPtr(block, 0, filterName);
                        Marshal.WriteIntPtr(block, IntPtr.Size, filterSpec);

                        try
                        {
                            dialog.SetFileTypes(1, block);
                            dialog.GetFileTypeIndex(out uint index);
                            Check(index >= 1, $"SetFileTypes / GetFileTypeIndex 往返（当前第 {index} 项）");
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(block);
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(filterName);
                        Marshal.FreeHGlobal(filterSpec);
                    }

                    // 3. 起始目录：走 SHCreateItemFromParsingName + SetFolder 那条路径，
                    //    并把路径读回来核对（同一套 IShellItem 接口供两处用）。
                    string startAt = Path.GetTempPath();
                    SetStartFolder(dialog, startAt);
                    Check(true, "SetDefaultFolder / SetFolder 可调用");

                    Guid shellItemIid = IidShellItem;

                    if (SHCreateItemFromParsingName(startAt, IntPtr.Zero, ref shellItemIid, out IShellItem? item) == 0 &&
                        item is not null)
                    {
                        try
                        {
                            string roundTrip = ReadPath(item);

                            Check(
                                string.Equals(
                                    Path.TrimEndingDirectorySeparator(roundTrip),
                                    Path.TrimEndingDirectorySeparator(startAt),
                                    StringComparison.OrdinalIgnoreCase),
                                $"IShellItem::GetDisplayName 读到完整路径（{roundTrip}）");
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(item);
                        }
                    }
                    else
                    {
                        failures.Add("SHCreateItemFromParsingName 失败");
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(dialog);
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (ownsCom)
                {
                    CoUninitialize();
                }
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!thread.Join(TimeSpan.FromSeconds(30)))
        {
            Console.WriteLine("  [FAIL] 自检超时（COM 调用挂住了）");
            failures.Add("自检超时");
        }

        Console.WriteLine();

        if (failures.Count > 0)
        {
            Console.WriteLine($"RESULT: FAILED ({failures.Count} 项)");

            foreach (string failure in failures)
            {
                Console.WriteLine($"  - {failure}");
            }

            return 1;
        }

        Console.WriteLine("RESULT: OK");
        return 0;
    }

    private static void Check(bool condition, string what) =>
        Console.WriteLine($"  [{(condition ? "ok" : "FAIL")}] {what}");

    /* ---------------------------------------------------------------- COM 声明 */


    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(
        ref Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IFileOpenDialog ppv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHCreateItemFromParsingName(
        string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    /// <summary>
    /// IFileOpenDialog 的完整 vtable，**平铺声明**（不依赖接口继承）。
    ///
    /// COM 是按 vtable 槽位调用的：方法顺序错一个，就会调到完全不相干的函数上去，
    /// 症状通常是"设进去的值读出来不对"或莫名的 E_INVALIDARG（映射成 ArgumentException）。
    /// 实测踩过一次——继承 IModalWindow 时槽位整体错了一位，SetOptions 实际调到了
    /// SetFileTypeIndex。所以这里把 IUnknown 之后的方法按 Windows SDK 头文件的顺序
    /// 全部列出来，不继承、不省略：
    ///
    ///   IModalWindow.Show
    ///   IFileDialog 的 23 个方法
    ///   IFileOpenDialog 自己的 GetResults / GetSelectedItems
    ///
    /// 自检用 --selftest-picker，会把设进去的选项读回来核对（不需要人点对话框）。
    /// </summary>
    [ComImport, Guid("D57C7288-D4AD-4768-BE02-9D969532D960"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        // ---- IModalWindow ----

        // 用户取消时返回 HRESULT_FROM_WIN32(ERROR_CANCELLED)，那是正常结果而不是异常，
        // 所以这里自己判返回值。
        [PreserveSig]
        int Show(IntPtr parent);

        // ---- IFileDialog ----
        //
        // 注意：**没用到的方法也必须逐个声明**。COM 按槽位调用，省略任何一个，
        // 它后面的所有方法都会往前错一位，调到不相干的函数上去（实测踩过：
        // SetOptions 实际调到了 SetFileTypeIndex，报 E_INVALIDARG）。
        // 所以下面这些即使当前没用，也不能删。

        void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);

        void SetFileTypeIndex(uint iFileType);

        void GetFileTypeIndex(out uint piFileType);

        void Advise(IntPtr pfde, out uint pdwCookie);

        void Unadvise(uint dwCookie);

        void SetOptions(uint fos);

        void GetOptions(out uint pfos);

        void SetDefaultFolder(IShellItem psi);

        void SetFolder(IShellItem psi);

        void GetFolder(out IShellItem ppsi);

        void GetCurrentSelection(out IShellItem ppsi);

        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        void GetFileName(out IntPtr pszName);

        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);

        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);

        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);

        void GetResult(out IShellItem ppsi);

        void AddPlace(IShellItem psi, int fdap);

        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);

        void Close(int hr);

        void SetClientGuid(ref Guid guid);

        void ClearClientData();

        void SetFilter(IntPtr pFilter);

        // ---- IFileOpenDialog ----
        //
        // 这两个方法（以及它们用到的 IShellItemArray）当前没被调用，但**必须保留**：
        // 它们是 vtable 的最后两槽，删掉不影响前面的调用，但留着能让这份声明与 SDK
        // 头文件逐行对应，便于日后核对。参数类型只需要能通过编译（我们从不构造它）。

        void GetResults(out IShellItemArray ppenum);

        void GetSelectedItems(out IShellItemArray ppsai);
    }

    /// <summary>
    /// IShellItemArray 的占位声明。
    ///
    /// 只用来让 <see cref="IFileOpenDialog.GetResults"/> 的参数类型成立；编辑器不调用它，
    /// 所以不需要真的按 vtable 声明方法（真的要用时得按 SDK 顺序补齐）。
    /// </summary>
    [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray
    {
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);

        void GetParent(out IShellItem ppsi);

        void GetDisplayName(uint sigdnName, out IntPtr ppszName);

        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

}
