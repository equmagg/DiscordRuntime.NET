
namespace DiscordRuntime
{
    using Discord.Commands;
    using Discord;
    using Discord.WebSocket;
    using Discord.Interactions;
    using Microsoft.Extensions.DependencyInjection;
    using System.Threading.Tasks;
    using System.Reflection;
    using static DiscordRuntime.RuntimeClient;
    using System;
    #region Attributes
    public class RequireGuildAttribute : Discord.Commands.PreconditionAttribute
    {
        private readonly ulong _allowedGuildId;

        public RequireGuildAttribute(ulong allowedGuildId)
        {
            _allowedGuildId = allowedGuildId;
        }

        public override Task<Discord.Commands.PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            if (context.Guild?.Id != _allowedGuildId)
            {
                return Task.FromResult(Discord.Commands.PreconditionResult.FromError($"This command is only availible at the server with ID {_allowedGuildId}."));
            }

            return Task.FromResult(Discord.Commands.PreconditionResult.FromSuccess());
        }
    }
    [AttributeUsage(AttributeTargets.Method)]
    public class ButtonHandlerAttribute : Attribute
    {
        public string Id { get; }
        public ButtonHandlerAttribute(string id) => Id = id;
    }
    [AttributeUsage(AttributeTargets.Method)]
    public class MessageHandlerAttribute : Attribute
    {

    }
    [AttributeUsage(AttributeTargets.Method)]
    public class UserJoinedAttribute : Attribute
    {

    }
    [AttributeUsage(AttributeTargets.Method)]
    public class UserLeftAttribute : Attribute
    {

    }
    [AttributeUsage(AttributeTargets.Method)]
    public class ModalHandlerAttribute : Attribute
    {
        public string Id { get; }
        public ModalHandlerAttribute(string id) => Id = id;
    }
    #endregion
    public static class RuntimeClient
    {
        /// <summary>
        /// Discord client static context for reference
        /// </summary>
        public static class InitializeContext
        {
            public static DiscordSocketClient? Client { get; internal set; }
            public static CommandService? CommandsService { get; internal set; }
            public static IServiceProvider? Services { get; internal set; }
            public static InteractionService? InteractionService { get; internal set; }
            public static string? Prefix { get; internal set; }
            public static Dictionary<ulong, bool> ContextLoaded { get; internal set; } = new Dictionary<ulong, bool>();

    }
        /// <summary>
        /// Starts Discord client and initializes baseline services
        /// </summary>
        public static async Task<(DiscordSocketClient Client, CommandService Commands, IServiceProvider Services)> BaseInitializeAsync(string token, string prefix, GatewayIntents gatewayIntents)
        {
            DiscordSocketConfig configStart = new DiscordSocketConfig();
            configStart.GatewayIntents = gatewayIntents;
            DiscordSocketClient client = new DiscordSocketClient(configStart);
            InitializeContext.Client = client;
            CommandService commands = new CommandService();
            IServiceProvider services = new ServiceCollection().AddSingleton(client).AddSingleton(commands).AddSingleton<InteractionService>(provider =>
            new InteractionService(client, new InteractionServiceConfig())).BuildServiceProvider(); 
            InteractionService interactionService = services.GetRequiredService<InteractionService>();
            await interactionService.AddModulesAsync(Assembly.GetEntryAssembly(), services);
            await commands.AddModulesAsync(Assembly.GetEntryAssembly(), services);
            await client.LoginAsync(TokenType.Bot, token);
            await client.StartAsync();
            InitializeContext.CommandsService = commands;
            InitializeContext.Services = services;
            InitializeContext.InteractionService = interactionService;
            InitializeContext.Prefix = prefix;
            return (client, commands, services);
        }
        /// <summary>
        /// Starts Discord client, initializes baseline services, subscribes to main events and maps attribute methods to events.
        /// </summary>
        /// <remarks>
        /// Ready to go solution with premade settings.
        /// </remarks>
        public static async Task<(DiscordSocketClient Client, CommandService Commands, IServiceProvider Services)> FastInitializeAsync(string token, string prefix = "!", GatewayIntents? gatewayIntents = null)
        {
            gatewayIntents ??= GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent;
            var ctx = await BaseInitializeAsync(token, prefix, (GatewayIntents)gatewayIntents);
            ctx.Client.Log += (msg) => { Console.WriteLine(msg); return Task.CompletedTask; };
            ctx.Client.Disconnected += async (arg) =>
            {
                _ = Task.Run(async () => {
                    await Task.Delay(50);
                    Console.WriteLine(arg.ToString() + "\nRestarting...");
                    await ctx.Client.StartAsync();
                }).ConfigureAwait(false);
            };
            await MapMethods.Map();
            ctx.Client.MessageReceived += (arg) =>
            {

                int argPos = 0;
                var message = arg as SocketUserMessage;
                if (message.Author.IsBot)
                    return Task.CompletedTask;
                if (message.HasStringPrefix(prefix, ref argPos))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var result = await ctx.Commands.ExecuteAsync(new SocketCommandContext(ctx.Client, message), argPos, ctx.Services).ConfigureAwait(false);
                        }
                        catch (Exception e) { Console.WriteLine(e.ToString()); }
                    });
                }
                return Task.CompletedTask;
            };
            return ctx;
        }
    }
    /// <summary>
    /// Maps attribute-marked methods to Discord-specific events
    /// </summary>
    internal static class MapMethods
    {
        internal static async Task Map(ulong? guildId = null)
        {
            Dictionary<string, Action<string, ulong>> _buttonMethods = new();
            Dictionary<string, Action<SocketModal>> _modalMethods = new();
            object? _dynamicInstance;
            Action<SocketMessage>? _messageHandlerMethod = null;
            Action<SocketGuildUser>? _userJoinedMethod = null;
            Action<SocketGuild, SocketUser>? _userLeftMethod = null;
            LoadButtonHandlers(ref _messageHandlerMethod, ref _userJoinedMethod, ref _userLeftMethod, ref _buttonMethods, ref _modalMethods, guildId);
            RuntimeClient.InitializeContext.Client.ModalSubmitted += async (modal) =>
            {
                if (_modalMethods.ContainsKey(modal.Data.CustomId))
                {
                    await modal.DeferAsync().ConfigureAwait(false);
                    _modalMethods[modal.Data.CustomId]?.Invoke(modal);
                }
                else
                {
                    //Console.WriteLine("unknown modal: " + modal.Data.CustomId);
                }
            };
            RuntimeClient.InitializeContext.Client.ButtonExecuted += async (component) => 
            {
                try
                {
                    ulong serverId = component.GuildId ?? default;
                    if (guildId != serverId && guildId != null) return;
                    string buttonId = component.Data.CustomId;
                    if (!buttonId.Contains(InitializeContext.Prefix)) return;
                    if (component.HasResponded)
                    {
                        return;
                    }
                    var userToClick = component.User.Id;
                    string arg = null;
                    if (buttonId.Contains("#ButtonArgs#"))
                    {
                        buttonId = component.Data.CustomId.Split("#ButtonArgs#")[0];
                        arg = component.Data.CustomId.Split("#ButtonArgs#")[1];
                    }

                    buttonId = buttonId.Replace(InitializeContext.Prefix, "");
                    buttonId = buttonId.Replace("<#dynamic#>", "");
                    string guild = null;
                    if (_buttonMethods.ContainsKey(buttonId))
                    {

                        _buttonMethods[buttonId]?.Invoke(arg, serverId);
                        if(_modalMethods.ContainsKey(buttonId)) DynamicLoaderContextStatic.SubmitModal(component, buttonId); else
                        await component.DeferAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        //Console.WriteLine($"unknown button: {buttonId}");
                    }


                }
                catch (Exception ex)
                { Console.WriteLine($"Dinamyc button handler for {guildId}:" + ex.ToString()); }
            };
            RuntimeClient.InitializeContext.Client.MessageReceived += async (arg) =>
            {
                var message = arg as SocketUserMessage;
                if (message.Author.IsBot) return;
                ulong serverId = (message.Channel as SocketGuildChannel)?.Guild?.Id ?? 0;
                if ((serverId != guildId && guildId != null) || _messageHandlerMethod == null) return;

                _messageHandlerMethod?.Invoke(message);
            };
            RuntimeClient.InitializeContext.Client.UserJoined += async (user) =>
            {
                if ((user.Guild.Id != guildId && guildId != null) || _userJoinedMethod == null) return;
                _userJoinedMethod?.Invoke(user);
            };
            RuntimeClient.InitializeContext.Client.UserLeft += async (guild, user) =>
            {
                if ((guild.Id != guildId && guildId != null) || _userLeftMethod == null) return;
                _userLeftMethod?.Invoke(guild, user);
            };
        }

        internal static void LoadButtonHandlers(ref Action<SocketMessage>? _messageHandlerMethod, ref Action<SocketGuildUser>? _userJoinedMethod, ref Action<SocketGuild, SocketUser>? _userLeftMethod,
            ref Dictionary<string, Action<string, ulong>> _buttonMethods, ref Dictionary<string, Action<SocketModal>> _modalMethods, ulong? guildId)
        {
            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(x =>
                 !x.FullName.StartsWith("System.")
                 && !x.FullName.StartsWith("Newtonsoft.")
                 && !x.FullName.StartsWith("Discord.")
                 && !x.FullName.StartsWith("Microsoft.")).ToArray();
                //Console.WriteLine($"Found {assemblies.Length} assemblies. in {AppDomain.CurrentDomain.FriendlyName}");

                foreach (var assembly in assemblies)
                {
                    //Console.WriteLine($"Found {assembly.FullName}");
                    if (assembly.FullName.Contains("Dynamic") || guildId == null)
                    {
                        var types = assembly.GetTypes();

                        foreach (var type in types)
                        {
                            //Console.WriteLine($"Checking type: {type.Name}");
                            if (type.Name.Contains("Module_") || guildId == null)
                            {
                                var buttonMethods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic)
                                     .Where(m => m.GetCustomAttribute<ButtonHandlerAttribute>() != null);
                                var modalMethods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic)
                                     .Where(m => m.GetCustomAttribute<ModalHandlerAttribute>() != null);
                                var methodMsg = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic)
                                     .FirstOrDefault(m => m.GetCustomAttribute<MessageHandlerAttribute>() != null);
                                var methodUserJoined = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic)
                                     .FirstOrDefault(m => m.GetCustomAttribute<UserJoinedAttribute>() != null);
                                var methodUserLeft = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic)
                                     .FirstOrDefault(m => m.GetCustomAttribute<UserLeftAttribute>() != null);
                                if (methodMsg != null)
                                {
                                    //Console.WriteLine($"Found MessageHandler method: {methodMsg.Name}");
                                    if (methodMsg.GetParameters().Length == 1 && methodMsg.GetParameters()[0].ParameterType == typeof(SocketMessage) && methodMsg.ReturnType == typeof(void))
                                    {
                                        try
                                        {
                                            var methodDelegate = (Action<SocketMessage>)Delegate.CreateDelegate(typeof(Action<SocketMessage>), methodMsg);
                                            _messageHandlerMethod = methodDelegate;
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"Error creating delegate for method {methodMsg.Name}: {ex.Message}");
                                        }
                                    }
                                    else
                                    {
                                        Console.WriteLine($"Method {methodMsg.Name} does not match the expected signature.");
                                    }
                                }
                                if (methodUserJoined != null)
                                {
                                    if (methodUserJoined.GetParameters().Length == 1 && methodUserJoined.GetParameters()[0].ParameterType == typeof(SocketGuildUser) && methodUserJoined.ReturnType == typeof(void))
                                    {
                                        try
                                        {
                                            var methodDelegate = (Action<SocketGuildUser>)Delegate.CreateDelegate(typeof(Action<SocketGuildUser>), methodUserJoined);
                                            _userJoinedMethod = methodDelegate;
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"Error creating delegate for method {methodUserJoined.Name}: {ex.Message}");
                                        }
                                    }
                                    else
                                    {
                                        Console.WriteLine($"Method {methodUserJoined.Name} does not match the expected signature.");
                                    }
                                }
                                if (methodUserLeft != null)
                                {
                                    if (methodUserLeft.GetParameters().Length == 1 && methodUserLeft.GetParameters()[0].ParameterType == typeof(SocketGuild) && methodUserLeft.GetParameters()[1].ParameterType == typeof(SocketUser) && methodUserLeft.ReturnType == typeof(void))
                                    {
                                        try
                                        {
                                            var methodDelegate = (Action<SocketGuild, SocketUser>)Delegate.CreateDelegate(typeof(Action<SocketGuild, SocketUser>), methodUserLeft);
                                            _userLeftMethod = methodDelegate;
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"Error creating delegate for method {methodUserLeft.Name}: {ex.Message}");
                                        }
                                    }
                                    else
                                    {
                                        Console.WriteLine($"Method {methodUserLeft.Name} does not match the expected signature.");
                                    }
                                }
                                foreach (var method in buttonMethods)
                                {
                                    var attr = method.GetCustomAttribute<ButtonHandlerAttribute>();
                                    if (attr != null)
                                    {
                                        //Console.WriteLine($"Adding button method {method.Name} with ID: {attr.Id}");
                                        if (method.GetParameters().Length == 2 && method.GetParameters()[0].ParameterType == typeof(string) && method.GetParameters()[1].ParameterType == typeof(ulong) && method.ReturnType == typeof(void))
                                        {
                                            try
                                            {
                                                var methodDelegate = (Action<string, ulong>)Delegate.CreateDelegate(typeof(Action<string, ulong>), null, method);
                                                _buttonMethods[attr.Id] = methodDelegate;
                                            }
                                            catch (Exception ex)
                                            {
                                                Console.WriteLine($"Error creating delegate for method {method.Name}: {ex.Message}");
                                            }
                                        }
                                        else
                                        {
                                            Console.WriteLine($"Method {method.Name} does not match the expected signature for Action." + method.ReturnType.Name);
                                        }
                                    }
                                }

                                foreach (var method in modalMethods)
                                {
                                    var attr = method.GetCustomAttribute<ModalHandlerAttribute>();
                                    if (attr != null)
                                    {
                                        //Console.WriteLine($"Adding modal method {method.Name} with ID: {attr.Id}");
                                        if (method.GetParameters().Length == 1 && method.GetParameters()[0].ParameterType == typeof(SocketModal)&& method.ReturnType == typeof(void))
                                        {
                                            try
                                            {
                                                var methodDelegate = (Action<SocketModal>)Delegate.CreateDelegate(typeof(Action<SocketModal>), null, method);
                                                _modalMethods[attr.Id] = methodDelegate;
                                            }
                                            catch (Exception ex)
                                            {
                                                Console.WriteLine($"Error creating delegate for method {method.Name}: {ex.Message}");
                                            }
                                        }
                                        else
                                        {
                                            Console.WriteLine($"Method {method.Name} does not match the expected signature for Action." + method.ReturnType.Name);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("LoadButtons: " + ex.ToString());
            }
        }
    }
    
}
