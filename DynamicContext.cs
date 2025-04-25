
namespace DiscordRuntime
{
    using Discord.Commands;
    using Discord;
    using Discord.WebSocket;
    using Discord.Interactions;
    using Microsoft.Extensions.DependencyInjection;
    using System.Threading.Tasks;
    using System.Reflection;
    using System;
    using static DiscordRuntime.RuntimeClient;
    using System.Diagnostics;

    public abstract class DynamicLoaderContextBase
    {
        protected ulong? GuildId { get; private set; } = null;
        protected DynamicLoaderContextBase(ulong _id)
        {
            GuildId = _id;
            if (!InitializeContext.ContextLoaded.ContainsKey(_id) || InitializeContext.ContextLoaded[_id] == false)
            {
                InitializeContext.ContextLoaded[_id] = true;
                MapMethods.Map(_id).GetAwaiter().GetResult();
            }
        }
        protected DynamicLoaderContextBase()
        {
            if (RuntimeClient.InitializeContext.Client != null)
            {
                MapMethods.Map().GetAwaiter().GetResult();
            }
            else Console.WriteLine("Client null reference");
        }

    }
    public class DynamicLoaderContext : DynamicLoaderContextBase
    {
        public DynamicLoaderContext(ulong _id) : base(_id)
        {

        }
        public DynamicLoaderContext() : base()
        {

        }
        public virtual void Send(ISocketMessageChannel chnl, string str)
        {
            if (chnl is IGuildChannel guildChannel)
            {
                var guild = guildChannel.Guild;
                if (guild.Id == GuildId || GuildId == null) chnl.SendMessageAsync(str).GetAwaiter();
            }
        }

        public virtual void AddButton(ComponentBuilder component, string label, string customId, ButtonStyle btnStyle = ButtonStyle.Success, string link = null, string arg = null)
        {
            if (!string.IsNullOrEmpty(link))
                component.WithButton(label, null, style: ButtonStyle.Link, url: link);
            else if (!string.IsNullOrEmpty(arg))
                component.WithButton(label, InitializeContext.Prefix + "<#dynamic#>" + GuildId + customId + "#ButtonArgs#" + arg, style: btnStyle);
            else
                component.WithButton(label, InitializeContext.Prefix + "<#dynamic#>" + GuildId + customId, style: btnStyle);
        }



    }
    public static class DynamicLoaderContextStatic
    {
        private static DynamicLoaderContextStaticBase _Instance { get; set; } = new();
        public static void SetInstance(DynamicLoaderContextStaticBase instance)
        {
            _Instance = instance;
        }
        public static void AddButton(ComponentBuilder component, string label, string customId, ButtonStyle btnStyle = ButtonStyle.Success, string link = null, string arg = null)
        {
            _Instance.AddButton(component, label, customId, btnStyle, link, arg);
        }
        public static void SubmitModal(SocketMessageComponent component, string customId, string title = "Modal", string label = "Input:", string inputId = "input_field", string placeholder = "Type here...")
        {
            _Instance.SubmitModal(component, customId, title, label, inputId, placeholder).GetAwaiter().GetResult();
        }
        public static string Serialize<T>(T obj) => _Instance.Serialize(obj);
        public static T Deserialize<T>(string json) => _Instance.Deserialize<T>(json);
        public static ulong GetIdByPing(string str) => ulong.Parse(str.Replace("<@", "").Replace(">", ""));
        public static string GetModalInput(SocketModal modal, uint index) => _Instance.GetModalInput(modal, index);
        public static string GetModalInput(SocketModal modal) => _Instance.GetModalInput(modal);
        public static Discord.Color GetDiscordColor(int r, int g, int b) => _Instance.GetDiscordColor(r, g, b);
        public static Discord.IUserMessage AsEmbed(SocketCommandContext msg, string str = "Done", Discord.Color? color = null) => _Instance.AsEmbed(msg, str, color);
        public static Discord.IUserMessage AsEmbed(Discord.IUserMessage msg, string str = "Done", Discord.Color? color = null) => _Instance.AsEmbed(msg, str, color);
        public static Discord.IUserMessage AsEmbed(Discord.WebSocket.SocketUserMessage msg, string str = "Done", Discord.Color? color = null) => _Instance.AsEmbed(msg, str, color);
        public static Discord.IUserMessage AsEmbed(SocketMessage arg, string str = "Done", Discord.Color? color = null) => _Instance.AsEmbed(arg, str, color);
        public static Discord.IUserMessage Reply(Discord.IUserMessage msg, string str) => _Instance.Reply(msg, str);
        public static Discord.IUserMessage Reply(SocketCommandContext msg, string str) => _Instance.Reply(msg, str);
        public static EmbedBuilder GetEmbed(string str, Discord.Color? color = null) => _Instance.GetEmbed(str, color);
        public static void ModifyMessage(Discord.IUserMessage msg, Optional<Embed> embed, Optional<MessageComponent> component) { _Instance.ModifyMessage(msg, embed, component); }
        public static void ModifyMessage(Discord.IUserMessage msg, Action<MessageProperties> func, RequestOptions options = null) { _Instance.ModifyMessage(msg, func, options); }
        public static void ModifyMessage(Discord.IUserMessage msg, string content, Optional<MessageComponent> component) { _Instance.ModifyMessage(msg, content, component); }
        public static void ModifyMessage(Discord.IUserMessage msg, string content, ComponentBuilder componentBuilder) { _Instance.ModifyMessage(msg, content, componentBuilder); }
        public static IUserMessage GetMessageById(ulong id, ulong GuildId) => _Instance.GetMessageById(id, GuildId);
        public static ComponentBuilder GetComponent() => new ComponentBuilder();
        public static void Log(string str) { _Instance.Log(str); }
        public static void Delay(int ms) { _Instance.Delay(ms); }
    }
    public class DynamicLoaderContextStaticBase
    {
        public virtual void AddButton(ComponentBuilder component, string label, string customId, ButtonStyle btnStyle = ButtonStyle.Success, string link = null, string arg = null)
        {
            if (string.IsNullOrEmpty(customId) || customId.Contains("<#dynamic#>") || customId.Contains("<#dynamic#>") || customId.Contains(InitializeContext.Prefix)) throw new NotImplementedException("Contains used keywords");
            if (!string.IsNullOrEmpty(link))
                component.WithButton(label, null, style: ButtonStyle.Link, url: link);
            else if (!string.IsNullOrEmpty(arg))
                component.WithButton(label, InitializeContext.Prefix + "<#dynamic#>" + customId + "#ButtonArgs#" + arg, style: btnStyle);
            else
                component.WithButton(label, InitializeContext.Prefix + "<#dynamic#>" + customId, style: btnStyle);
        }
        public virtual async Task SubmitModal(SocketMessageComponent component, string customId, string title = "Modal", string label = "Input:", string inputId = "input_field", string placeholder = "Type here...")
        {
            var modalBuilder = new Discord.ModalBuilder().WithTitle(title).WithCustomId(customId)
                        .AddTextInput(label, inputId, placeholder: placeholder);
            await component.RespondWithModalAsync(modalBuilder.Build());
        }
        public virtual string Serialize<T>(T obj)
        {
            return System.Text.Json.JsonSerializer.Serialize(obj);
        }
        public virtual T Deserialize<T>(string json)
        {
            return System.Text.Json.JsonSerializer.Deserialize<T>(json);
        }
        public virtual string GetModalInput(SocketModal modal)
        {
            var e = modal.Data.Components.GetEnumerator();
            e.MoveNext();
            return e.Current.Value;
        }
        public virtual string GetModalInput(SocketModal modal, uint index)
        {
            var e = modal.Data.Components.GetEnumerator();
            for (int i = 0; i < index + 1; i++)
                e.MoveNext();
            return e.Current.Value;
        }
        #region embed

        public virtual Discord.Color GetDiscordColor(int r, int g, int b)
        {
            return new Discord.Color(r, g, b);
        }
        public virtual Discord.IUserMessage AsEmbed(SocketCommandContext msg, string str = "Done", Discord.Color? color = null)
        {
            if (color == null) color = Discord.Color.Green;
            var embed = new EmbedBuilder
            {
                Description = $"{str}",
                Color = color
            };
            var message = msg.Message.ReplyAsync(embed: embed.Build()).GetAwaiter().GetResult();
            return message;
        }
        public virtual Discord.IUserMessage AsEmbed(Discord.IUserMessage msg, string str = "Done", Discord.Color? color = null)
        {
            if (color == null) color = Discord.Color.Green;
            var embed = new EmbedBuilder
            {
                Description = $"{str}",
                Color = color
            };
            var message = msg.ReplyAsync(embed: embed.Build()).GetAwaiter().GetResult();
            return message;
        }
        public virtual Discord.IUserMessage AsEmbed(SocketMessage arg, string str = "Done", Discord.Color? color = null)
        {
            var msg = arg as SocketUserMessage;
            if (color == null) color = Discord.Color.Green;
            var embed = new EmbedBuilder
            {
                Description = $"{str}",
                Color = color
            };
            var message = msg.ReplyAsync(embed: embed.Build()).GetAwaiter().GetResult();
            return message;
        }
        public virtual Discord.IUserMessage AsEmbed(SocketUserMessage msg, string str = "Done", Discord.Color? color = null)
        {
            if (color == null) color = Discord.Color.Green;
            var embed = new EmbedBuilder
            {
                Description = $"{str}",
                Color = color
            };
            var message = msg.ReplyAsync(embed: embed.Build()).GetAwaiter().GetResult();
            return message;
        }
        public virtual Discord.IUserMessage Reply(Discord.IUserMessage msg, string str)
        {
            return msg.ReplyAsync(str).GetAwaiter().GetResult();
        }
        public virtual Discord.IUserMessage Reply(SocketCommandContext msg, string str)
        {
            return msg.Message.ReplyAsync(str).GetAwaiter().GetResult();
        }
        public virtual EmbedBuilder GetEmbed(string str, Discord.Color? color = null)
        {
            if (color == null) color = Discord.Color.Green;
            var embed = new EmbedBuilder
            {
                Description = $"{str}",
                Color = color
            };
            return embed;
        }

        #endregion
        #region modify message
        public virtual void ModifyMessage(Discord.IUserMessage msg, Optional<Embed> embed, Optional<MessageComponent> component)
        {
            msg.ModifyAsync(properties =>
            {
                properties.Embed = embed;
                properties.Components = component;

                properties.Components = component;

            }).GetAwaiter().GetResult();
        }
        public virtual void ModifyMessage(Discord.IUserMessage msg, Action<MessageProperties> func, RequestOptions options = null)
        {
            msg.ModifyAsync(func, options).GetAwaiter().GetResult();
        }
        public virtual void ModifyMessage(Discord.IUserMessage msg, string content, Optional<MessageComponent> component)
        {
            Embed embed;
            if (!string.IsNullOrEmpty(content))
                embed = (GetEmbed(content)).Build();
            else
                embed = msg.Embeds.FirstOrDefault() as Embed;
            msg.ModifyAsync(properties =>
            {
                properties.Embed = embed;
                properties.Components = component;

            }).GetAwaiter().GetResult();
        }
        public virtual void ModifyMessage(Discord.IUserMessage msg, string content, ComponentBuilder componentBuilder)
        {
            Embed embed;
            if (!string.IsNullOrEmpty(content))
                embed = (GetEmbed(content)).Build();
            else
                embed = msg.Embeds.FirstOrDefault() as Embed;
            var component = componentBuilder.Build();
            msg.ModifyAsync(properties =>
            {
                properties.Embed = embed;
                properties.Components = component;

            }).GetAwaiter().GetResult();
        }
        public virtual IUserMessage GetMessageById(ulong id, ulong GuildId)
        {
            var guild = InitializeContext.Client.GetGuild(GuildId);
            if (guild != null)
            {
                var textChannels = guild.TextChannels;
                foreach (var channel in textChannels)
                {
                    try
                    {
                        var message = channel.GetMessageAsync(id).GetAwaiter().GetResult();
                        if (message != null)
                        {
                            return message as IUserMessage;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error retrieving message from channel {channel.Name}: {ex.Message}");
                    }
                }
            }

            return null;

        }
        #endregion

        public virtual void Log(string str)
        {
            throw new NotImplementedException("Unsupported");
            Console.WriteLine(str);
        }
        public virtual void Delay(int ms)
        {
            throw new NotImplementedException("Unsupported");
            Task.Delay(ms).GetAwaiter().GetResult();
        }

        public virtual void UnloadAssembly()
        {
            throw new NotImplementedException("Unsupported");
            string exePath = Assembly.GetEntryAssembly().Location;
            Process.Start(exePath);
            Environment.Exit(-1);
        }
    }
}
