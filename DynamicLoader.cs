
namespace DiscordRuntime
{
    using Discord.Commands;
    using Discord.WebSocket;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Reactive.Joins;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Runtime.Loader;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    public class DynamicLoader
    {
        private readonly CommandService _commandService;
        private readonly IServiceProvider _services;
        private const int ExecutionTimeoutMs = 500;//500ms
        private const long MemoryLimitBytes = 100 * 1024 * 1024; // 100MB
        private const double CpuLimitPercentage = 30.0;//30% CPU
        private const long maxLength = 16000000;//16 000 000
        private readonly List<Type> _loadedModules = new();
        private Dictionary<string, DynamicModuleLoadContext> _loadContext = new();
        private Dictionary<string, Assembly> _assembly = new();
        private Dictionary<string, Type> _moduleType = new();
        public DynamicLoader(CommandService commandService, IServiceProvider services)
        {
            _commandService = commandService;
            _services = services;
        }
        /// <summary>
        /// Looks for Runtime dll path on your system.
        /// </summary>
        public string GetSystemRuntimePath()
        {
            string runtime = typeof(object).Assembly.Location;
            string[] possibleDotnetRoots = null;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                possibleDotnetRoots = new string[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.NETCore.App"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet", "shared", "Microsoft.NETCore.App"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "dotnet", "shared", "Microsoft.NETCore.App"),
                    @"C:\Program Files\dotnet\shared\Microsoft.NETCore.App",
                    @"C:\Program Files (x86)\dotnet\shared\Microsoft.NETCore.App"
                };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                possibleDotnetRoots = new string[]
                {
                    "/root/ProgramFiles/dotnet/shared/Microsoft.NETCore.App",
                    "/usr/share/dotnet/shared/Microsoft.NETCore.App",
                    "/usr/local/share/dotnet/shared/Microsoft.NETCore.App",
                    "/opt/dotnet/shared/Microsoft.NETCore.App"
                };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                possibleDotnetRoots = new string[]
                {
                    "/usr/local/share/dotnet/shared/Microsoft.NETCore.App",
                    "/usr/share/dotnet/shared/Microsoft.NETCore.App"
                };
            }
            foreach (var dotnetRoot in possibleDotnetRoots)
            {
                if (Directory.Exists(dotnetRoot))
                {
                    var versions = Directory.GetDirectories(dotnetRoot)
                        .OrderByDescending(v => v)
                        .ToArray();

                    string[] searchVersions = new string[] { "6.0.36", "8.0.5", "8.0", "6.0.30", "6.0", "5.0", "3.1" };

                    foreach (var version in searchVersions)
                    {
                        var versionDir = versions.FirstOrDefault(v => v.Contains(version));
                        if (versionDir != null)
                        {
                            runtime = Path.Combine(versionDir, "System.Runtime.dll");
                            return runtime;
                        }
                    }


                }
            }

            return runtime;
        }
        /// <summary>
        /// Adds some safety measures to prevent abuse of dynamic environment.
        /// </summary>
        /// <remarks>
        /// Keep in mind, is does not make your computer abuse-proof, just secures implementation-specific measures.
        /// </remarks>
        public static string TransformCodeSafety(string str)
        {
            str = str.Replace("namespace", "")
             .Replace("<#module#>", "")
             .Replace("<#id#>", "")
             .Replace("RequireGuild", "")
             .Replace("DynamicLoaderContext", "")
             .Replace("DynamicLoader", "")
             .Replace("using", "");
            //Delete Comments
            str = (new Regex(@"(?<!\bhttp:|\bhttps:)//.*?$|/\*[\s\S]*?\*/", RegexOptions.Multiline)).Replace(str, string.Empty);
            //Delete async
            str = Regex.Replace(str, @"\b(await|TaskCompletionSource|Task|ThreadPool|Thread|Parallel|AsyncLocal|ConfigureAwait|Timer|SpinWait|Mutex|ExecutionContext)\b", "/*Async method*/");
            str = Regex.Replace(str, @"await\s+(?<name>\w+)\.ModifyAsync\(\s*(?<lambda>\w+)\s*=>\s*\{(?<body>(?>[^{}]+|\{(?<DEPTH>)|\}(?<-DEPTH>))*(?(DEPTH)(?!)))\}\)", @"ModifyMessage(${name}, ${lambda} => {${body}})", RegexOptions.Singleline);

            // Direct Discord.Net type blockage
            str = Regex.Replace(str, @"\b(Discord\.Commands|Discord\.WebSocket|Discord\.Interactions|IUser|IGuild|SocketChannel|SocketClient|SocketUser|SocketSelfUser)\b",
            "/* Discord.Net usage is blocked */", RegexOptions.IgnoreCase);
            str = Regex.Replace(str, @"\b(SocketCommandContext)\b", "Context", RegexOptions.IgnoreCase);
            //Dangerous Discord Methods blockage
            str = Regex.Replace(str, @"\b(SocketClient|SocketChannel|SocketUser|IUser|SocketGuildUser)\.(LoginAsync|StartAsync|StopAsync|SendMessageAsync|DeleteAsync|KickAsync|BanAsync|CreateInviteAsync|AddRoleAsync|RemoveRoleAsync|ModifyAsync)\b",
            "/* Method call blocked */", RegexOptions.IgnoreCase);
            
            //modules delete
            str = (new Regex(@"<#[^#]*#>")).Replace(str, string.Empty);
            return str;
        }
        /// <summary>
        /// Auto-conversion methods and shortcuts, simplifying migration to dynamic environment.
        /// </summary>
        public static string TransformCodeConviniency(string str)
        {
            //Add Context shortcuts
            str = Regex.Replace(str, @"AsEmbed\(\s*\$?""(.*?)""\s*\)", @"AsEmbed(Context, ""$1"")");//Add Context
            str = Regex.Replace(str, @"Reply\(\s*\$?""(.*?)""\s*\)", @"Reply(Context, ""$1"")");//Add Context
            str = str.Replace("new ComponentBuilder()", "GetComponent()");
            //Automatic async Task
            var matches = Regex.Matches(str, @"(?<=\])\s*(?!\s*\[)[\s\S]*?(?=\{)");
            foreach (Match match in matches)
            {
                if (!match.Value.Contains("public") && !match.Value.Contains("async") && !match.Value.Contains("Task") && !match.Value.Contains(";"))
                {
                    str = str.Replace(match.Value, "\npublic async Task " + match.Value);
                }
            }
            str = Regex.Replace(str, @"(?<=^|\s)AddButton\(", @"d.AddButton(");//Convert from static

            return str;
        }
        /// <summary>
        /// Wraps command code into implementation-specific class.
        /// </summary>
        /// <remarks>
        /// Nessesary for features to work properly
        /// </remarks>
        public static string TransformCodeNessesaryWrapping(string str, string server, string staticContextClass = null, string contextClass = null)
        {
            //Wrap code as class
            string staticCustomcontext = staticContextClass is null ? "" : "using static "+ staticContextClass + ";\n";
            string customcontext = contextClass is null ? "" : "private readonly " + contextClass + " x = new "+contextClass+"("+server+");\n";
            str = "using System;\n" +
                "using System.Collections.Generic;\n" +
                "using System.Threading;\n" +
                "using System.Threading.Tasks;\n" +
                "using Discord.Commands;\n" +
                "using Discord.WebSocket;\n" +
                "using DiscordRuntime;\n" +
                "using static DiscordRuntime.DynamicLoaderContextStatic;\n" + staticCustomcontext +
                "namespace DynamicModule" + server + "\n" +
                "{\npublic class Module_" + server + " : ModuleBase<SocketCommandContext>\n" +
                "{\nprivate readonly DiscordRuntime.DynamicLoaderContext d  = new DiscordRuntime.DynamicLoaderContext(" + server + ");\n" + customcontext +
                "public Module_"+server+"() { }\n"+str;

            //Add server prefix to buttons
            str = str.Replace("ButtonInterface", "ButtonInterface" + server);
            //Add required guild attribute
            if (!string.IsNullOrEmpty(server))
                str = Regex.Replace(str, @"(\[Command\("".*?""\)])", $"\n[RequireGuild({server})]\n$1");
            //id to buttons
            str = Regex.Replace(str, @"ButtonHandler\(""([^""]+)""\)", match =>
            {
                string originalValue = match.Groups[1].Value;
                return $"ButtonHandler(\"{server}{originalValue}\")";
            });
            //id to modals
            str = Regex.Replace(str, @"ModalHandler\(""([^""]+)""\)", match =>
            {
                string originalValue = match.Groups[1].Value;
                return $"ModalHandler(\"{server}{originalValue}\")";
            });

            str += "\n\n}\n}";
            return str;
        }
        /// <summary>
        /// Wraps every method`s code into a thread, which will be interrupted after timeout. 
        /// Watch out, as it requires a completly sync environment to work properly.
        /// </summary>
        /// <remarks>
        /// Method does everything it can to insure that, but there are ways to bypass it, f.e. when thread is never blocked and never waits to be interrupted
        /// </remarks>
        public static string TransformCodeTimeout(string str, uint timeout) //look for method types
        {
            //WrapMethods with Timeout
            string pattern = @"(?<Modifiers>\b(?:public|private|protected|internal|static|async|override|virtual|sealed|abstract)\s+)*" +
                 @"(?:(?<ReturnType>\w+(\s*<[^>]+>)?)\s+|(?=\b\w+\s*\())" +
                 @"(?<MethodName>\w+)\s*" +
                 @"\((?<Parameters>[^)]*)\)\s*" +
                 @"(?<Constraints>where[^({]*)?\s*" +
                 @"(?<Body>{(?:[^{}]*|(?<DEPTH>{)|(?<-DEPTH>}))*?(?(DEPTH)(?!))})";

            str = str.Replace("returnResult", "/*used keyword*/");
            str = Regex.Replace(str, pattern, match =>
            {
                string body = match.Groups["Body"].Value;
                string innerBody = Regex.Replace(body.Substring(1, body.Length - 2), @"\b(await|TaskCompletionSource|Task|ThreadPool|Thread|Parallel|AsyncLocal|ConfigureAwait|Timer|SpinWait|Mutex|ExecutionContext)\b", "/*Async method*/");
                string signature = match.Groups["Modifiers"].Value + match.Groups["ReturnType"].Value + " " + match.Groups["MethodName"].Value + " " + match.Groups["Constraints"].Value + "(" + match.Groups["Parameters"].Value + ")";
                if (string.IsNullOrEmpty(match.Groups["ReturnType"].Value.Trim())) { return signature+"\n"+body; }
                string returnVar = (match.Groups["ReturnType"].Value == "Task" || match.Groups["ReturnType"].Value == "void") ? "" : match.Groups["ReturnType"].Value.Replace("?", "") + "? returnResult = null;";
                if (returnVar.Contains("Task<")) returnVar = returnVar.Replace("Task<", "")[..^1];
                string replacementBefore = "\n{\n " + returnVar + " var thread = new Thread(DoWork);\n thread.Start();\n if(!thread.Join(" + timeout + "))\n {\n   thread.Interrupt();\n }\n void DoWork()\n {\n   try\n   {\n    lock(new object())\n    { ";
                string returnBody = "";
                if (innerBody.Contains("return;")) returnBody = "return;";
                else if (innerBody.Contains("return ")) returnBody = "return returnResult;";
                innerBody = Regex.Replace(innerBody, @"return\s+([^;]+)\s*;", match =>
                {
                    string returnValue = match.Groups[1].Value;
                    return "{returnResult = " + returnValue + "; return;}";
                });
                string replacementAfter = "    }\n   }\n   catch (ThreadInterruptedException)\n   {  }\n }" + returnBody + "\n}";
                return $"{signature}{replacementBefore}\n{innerBody}\n{replacementAfter}";
            }, RegexOptions.Singleline);
            str = Regex.Replace(str, @"(\b(while|for|foreach|do)\b\s*\([^)]*\)\s*\{)|(\bdo\b\s*\{)", "$1$3\nThread.Sleep(1);\n");
            return str;
        }
        /// <summary>
        /// Full code conversion for dynamic environment.
        /// </summary>
        public static string TransformCode(string str, string server = "", uint timeout = 3000, string staticContextClass = null, string contextClass = null)
        {
            str = TransformCodeSafety(str);
            str = TransformCodeConviniency(str);
            str = TransformCodeNessesaryWrapping(str, server, staticContextClass, contextClass);
            str = TransformCodeTimeout(str, timeout);
            
            //Console.WriteLine(str);
            return str;
        }
        /// <summary>
        /// Analyses the code and compiles it without executing one 
        /// </summary>
        /// <param name="server">Guild id for isolation</param>
        /// <param name="runtime">.NET Runtime Dll path</param>
        /// <param name="metadataReferences">using references</param>
        /// <param name="safety">toggles library blockage for safety concerns</param>
        public async Task<(string, CSharpCompilation?)> PreCompile(string code, string server = "", string runtime = null, IEnumerable<MetadataReference> metadataReferences = null, bool safety = true)
        {
            try
            {
                if (code.Length > maxLength) throw new InvalidOperationException($"Symbvol limit reached ({maxLength})");
                var syntaxTree = CSharpSyntaxTree.ParseText(code);
                var root = syntaxTree.GetRoot();

                if (safety)
                {
                    var blockedNamespaces = new[] { "System.IO", "System.Diagnostics", "System.Net", "System.Security.Cryptography",
                    "System.Threading.Channels", "System.Runtime.InteropServices", "System.Management", "Microsoft.Win32",
                    "System.Reflection", "System.Drawing", "System.Windows.Forms", "System.Net.Sockets", "System.Web", "System.IO.Pipes", "System.Threading.Thread", "System.Threading.Timer",
                    "System.Threading.Mutex", "System.Threading.Semaphore", "System.Threading.ThreadPool", "System.Threading.ManualResetEvent", "System.Threading.AutoResetEvent"
                };
                    var usingDirectives = root.DescendantNodes().OfType<UsingDirectiveSyntax>();

                    foreach (var usingDirective in usingDirectives)
                    {
                        if (blockedNamespaces.Any(namespaceName => usingDirective.Name.ToString().StartsWith(namespaceName)))
                        {
                            throw new InvalidOperationException($"The namespace '{usingDirective.Name}' is blocked.");
                        }
                    }


                    var dangerousNodes = root.DescendantNodes().Where(node =>
            node is InvocationExpressionSyntax invocation &&
            (invocation.ToString().Contains("Process.Start") ||
             invocation.ToString().Contains("File") ||
             invocation.ToString().Contains("Directory") ||
             invocation.ToString().Contains("Registry") ||
             invocation.ToString().Contains("WebRequest") ||
             invocation.ToString().Contains("Socket") ||
             invocation.ToString().Contains("HttpClient") ||
             invocation.ToString().Contains("FtpWebRequest") ||
             invocation.ToString().Contains("GC.Collect") ||
             invocation.ToString().Contains("Marshal") ||
             invocation.ToString().Contains("WindowsIdentity.GetCurrent") ||
             invocation.ToString().Contains("Assembly.Load") ||
             invocation.ToString().Contains("DllImport") ||
             invocation.ToString().Contains("Cryptography") ||
             invocation.ToString().Contains("System.Diagnostics.Process.Start") ||
             invocation.ToString().Contains("System.Diagnostics.Process.GetProcesses") ||
             invocation.ToString().Contains("System.Diagnostics.Process.GetCurrentProcess") ||
             invocation.ToString().Contains("System.Diagnostics") ||
             invocation.ToString().Contains("System.IO.StreamReader") ||
             invocation.ToString().Contains("System.IO.StreamWriter") ||
             invocation.ToString().Contains("Microsoft.Win32.Registry") ||
             invocation.ToString().Contains("Microsoft.Win32") ||
             invocation.ToString().Contains(".DeleteMessageAsync()") ||
             invocation.ToString().Contains(".SendMessageAsync") ||
             invocation.ToString().Contains(".ModifyAsync") ||
             invocation.ToString().Contains(".KickAsync()") ||
             invocation.ToString().Contains(".BanAsync()") ||
             invocation.ToString().Contains(".AddReactionAsync") ||
             invocation.ToString().Contains(".MuteAsync()") ||
             invocation.ToString().Contains(".DeafAsync()") ||
             invocation.ToString().Contains(".ModifyPermissionsAsync") ||
             invocation.ToString().Contains(".AddRoleAsync") ||
             invocation.ToString().Contains(".RemoveRoleAsync") ||
             invocation.ToString().Contains("Console.ReadLine") ||
             invocation.ToString().Contains("Console.ReadKey") ||

             (invocation.ToString().Contains("Task") &&
             !invocation.ToString().Contains("async") &&
             !invocation.ToString().Contains(".ConfigureAwait")) ||

            invocation.ToString().Contains(".ConfigureAwait(false)")
             )
             );

                    foreach (var node in dangerousNodes)
                    {
                        throw new InvalidOperationException("Unsafe method usage detected.");
                    }
                }
                metadataReferences ??= Enumerable.Empty<MetadataReference>();
                runtime ??= GetSystemRuntimePath();

                //Console.WriteLine(runtime);
                IEnumerable<MetadataReference> references = new[]
                {
                MetadataReference.CreateFromFile(runtime),
                MetadataReference.CreateFromFile(typeof(CommandService).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(SocketCommandContext).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(SocketMessage).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ICommandContext).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Discord.Interactions.InteractionService).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(DiscordSocketClient).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(DiscordRuntime.DynamicLoaderContext).Assembly.Location)
                };
                references = references.Concat(metadataReferences);
                var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
                var compilation = CSharpCompilation.Create(
                    "DynamicCommand" + server,
                    new[] { syntaxTree },
                    references,
                    compilationOptions
                );
                string res = "";
                var diagnostics = compilation.GetDiagnostics();
                var errorDiagnostics = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
                if (errorDiagnostics.Any())
                {
                    var ex = "";
                    foreach (var diagnostic in errorDiagnostics)
                    {
                        ex += ($"{diagnostic.Id}: {diagnostic.GetMessage()} (Line {diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1})\n");
                    }
                    res = ex;
                }
                else
                {
                    res = null;
                }
                return (res, compilation);
            }
            catch (Exception ex) { return (ex.ToString(), null); }
        }
        /// <summary>
        /// Compiles and executes a module from string.
        /// </summary>
        /// <param name="commandCode"></param>
        /// <param name="server"></param>
        /// <param name="runtimePath"></param>
        /// <param name="metadataReferences"></param>
        /// <param name="safety"></param>
        /// <returns>Returns true if successfull</returns>
        public async Task<bool> AddModuleAsync(string commandCode, string server = "", string runtimePath = null, IEnumerable<MetadataReference> metadataReferences = null, bool safety = true)
        {
            var res = await PreCompile(commandCode, server, runtimePath, metadataReferences, safety);
            var compilation = res.Item2;
            var ex = res.Item1;
            if (!string.IsNullOrEmpty(ex) || compilation == null) { Console.WriteLine(ex); return false; }
            bool success = await TryCompileModule(compilation);
            return success;
        }
        /// <summary>
        /// Transforms string to match expected layout, adds references to included extention classes, then compiles and executes a module.
        /// </summary>
        /// <param name="commandCode"></param>
        /// <param name="server"></param>
        /// <param name="timeout"></param>
        /// <param name="staticContextClass"></param>
        /// <param name="contextClass"></param>
        /// <param name="runtimePath"></param>
        /// <param name="metadataReferences"></param>
        /// <param name="safety"></param>
        /// <param name="logResult"></param>
        /// <returns></returns>
        public async Task<bool> AddModuleWithTrasformAsync(string commandCode, string server = "", uint timeout = 3000, string staticContextClass = null, string contextClass = null, string runtimePath = null, IEnumerable<MetadataReference> metadataReferences = null, bool safety = true, bool logResult = false)
        {
            try
            {
                string code = TransformCode(commandCode, server, timeout, staticContextClass, contextClass);
                if (logResult) Console.WriteLine(code);
                metadataReferences ??= Enumerable.Empty<MetadataReference>();
                if (staticContextClass != null)
                {
                    var type = AppDomain.CurrentDomain.GetAssemblies() .Select(a => a.GetType(staticContextClass, false)) .FirstOrDefault(t => t != null);
                    if (type == null)
                    {
                        Console.WriteLine($"Type '{staticContextClass}' not found.");
                        return false;
                    }
                    IEnumerable<MetadataReference> reference = new[]
                    {
                        MetadataReference.CreateFromFile(type.Assembly.Location),
                    };
                    metadataReferences = metadataReferences.Concat(reference);
                }
                if(contextClass != null)
                {
                    var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(contextClass, false)).FirstOrDefault(t => t != null);
                    if (type == null)
                    {
                        Console.WriteLine($"Type '{contextClass}' not found.");
                        return false;
                    }
                    IEnumerable<MetadataReference> reference = new[]
                    {
                        MetadataReference.CreateFromFile(type.Assembly.Location),
                    };
                    metadataReferences = metadataReferences.Concat(reference);
                }
                return await this.AddModuleAsync(code, server, runtimePath, metadataReferences, safety);
            }
            catch (Exception ex) { Console.WriteLine("AddModuleWithTrasformAsync error: \n" + ex); return false; }
        }
        /// <summary>
        /// Executes pre-compiled code
        /// </summary>
        /// <param name="compilation"></param>
        /// <returns></returns>
        public async Task<bool> TryCompileModule(CSharpCompilation compilation)
        {
            try
            {
                using var stream = new System.IO.MemoryStream();
                var result = compilation.Emit(stream);
                
                if (!result.Success)
                {
                    Console.WriteLine("Compilation failed!");
                    foreach (var diagnostic in result.Diagnostics)
                    {
                        Console.WriteLine($"{diagnostic.Id}: {diagnostic.GetMessage()}");
                    }
                    return false;
                }
                stream.Seek(0, System.IO.SeekOrigin.Begin);
                if (compilation.AssemblyName is null) return false;
                if(_loadContext.ContainsKey(compilation.AssemblyName))
                {
                    Console.WriteLine("Assembly with this name exists!"); return false;
                }
                _loadContext[compilation.AssemblyName] = new DynamicModuleLoadContext();
                _assembly[compilation.AssemblyName] = _loadContext[compilation.AssemblyName].LoadFromStream(stream);
                    _moduleType[compilation.AssemblyName] = _assembly[compilation.AssemblyName].GetTypes()
                        .FirstOrDefault(t => typeof(ModuleBase<SocketCommandContext>).IsAssignableFrom(t));

                if (_moduleType == null)
                {
                    Console.WriteLine("No valid module found.");
                    return false;
                }
                bool success;
                try
                {
                    await _commandService.AddModuleAsync(_moduleType[compilation.AssemblyName], _services);
                    success = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error executing dynamic module: {ex.Message}");
                    return false;
                }
                if (success)
                {
                    _loadedModules.Add(_moduleType[compilation.AssemblyName]);
                }
                return success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during dynamic module compilation or execution: {ex.Message}");
                return false;
            }
        }
        #region Unload
        /// <summary>
        /// Unloads all added command services from discord
        /// </summary>
        /// <remarks>Note, that it wont be able to unload an assembly</remarks>
        public async Task UnloadAllCommands()
        {
            foreach (var module in _loadedModules)
            {
                await _commandService.RemoveModuleAsync(module);
            }
            if (RuntimeClient.InitializeContext.ContextLoaded != null)
            {
                 foreach (var kvp in RuntimeClient.InitializeContext.ContextLoaded)
                {
                       RuntimeClient.InitializeContext.ContextLoaded[kvp.Key] = false;
                }
            }
            _loadedModules.Clear();

            var keysToUnload = _loadContext.Keys.ToList();
            foreach (var key in keysToUnload)
            {
                var alc = _loadContext[key];

                alc.Unload();

                _loadContext.Remove(key);
                _assembly.Remove(key);
                _moduleType.Remove(key);
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();


            Console.WriteLine("All dynamic commands have been unloaded.");
        }
        /// <summary>
        /// Unloads command service from discord by id
        /// </summary>
        /// <remarks>Note, that it wont be able to unload an assembly</remarks>
        public async Task UnloadCommandById(Type moduleType, ulong serverId = 0)
        {
            try
            {
                if (_loadedModules.Contains(moduleType))
                {
                    await _commandService.RemoveModuleAsync(moduleType);
                    _loadedModules.Remove(moduleType);
                    if (RuntimeClient.InitializeContext.ContextLoaded != null && RuntimeClient.InitializeContext.ContextLoaded.ContainsKey(serverId))
                        RuntimeClient.InitializeContext.ContextLoaded[serverId] = false;
                    Console.WriteLine($"Module {moduleType.Name} has been unloaded.");
                }
                else
                {
                    Console.WriteLine($"Module {moduleType.Name} not found.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error unloading module {moduleType.Name}: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Unloads command service from discord by id
        /// </summary>
        /// <remarks>Note, that it wont be able to unload an assembly</remarks>
        public async Task UnloadCommandById(string moduleName, ulong serverId)
        {
            try
            {
                moduleName += serverId.ToString();
                Type? moduleType = _loadedModules.FirstOrDefault(t => t.Name == moduleName);
                if (moduleType != null)
                {
                    if (_loadedModules.Contains(moduleType))
                    {
                        await _commandService.RemoveModuleAsync(moduleType);
                        _loadedModules.Remove(moduleType);
                        if (RuntimeClient.InitializeContext.ContextLoaded != null && RuntimeClient.InitializeContext.ContextLoaded.ContainsKey(serverId))
                            RuntimeClient.InitializeContext.ContextLoaded[serverId] = false;
                        Console.WriteLine($"Module {moduleType.Name} has been unloaded.");
                    }
                    else
                    {
                        Console.WriteLine($"Module {moduleType.Name} not found.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error unloading module {moduleName}: {ex.Message}");
            }
        }
        /// <summary>
        /// Unloads all duplicate command services from discord
        /// </summary>
        /// <remarks>Note, that it wont be able to unload an assembly</remarks>
        public async Task UnloadDuplicateModules(ulong serverId = 0)
        {
            try
            {
                var distinctModules = new HashSet<Type>();
                var duplicates = new List<Type>();

                foreach (var moduleType in _loadedModules)
                {
                    if (!distinctModules.Add(moduleType))
                        duplicates.Add(moduleType);
                }

                foreach (var moduleType in duplicates)
                {
                    if (_commandService.Modules.Any(m => m.GetType() == moduleType))
                    {
                        await _commandService.RemoveModuleAsync(moduleType);
                        Console.WriteLine($"Module {moduleType.Name} has been unloaded as duplicate.");
                    }
                    _loadedModules.Remove(moduleType);
                }


            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error unloading duplicate modules: {ex.Message}");
            }
        }
        #endregion
    }
    #region Assembly load context (alc)
    public class DynamicModuleLoadContext : AssemblyLoadContext
    {
        public DynamicModuleLoadContext() : base(isCollectible: true) { }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            return null;
        }
    }
    #endregion
}
