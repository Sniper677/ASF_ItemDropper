﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using ArchiSteamFarm;
using ArchiSteamFarm.Plugins;
using ArchiSteamFarm.Localization;
using JetBrains.Annotations;
using SteamKit2;

namespace ASFItemDropManager
{
    [Export(typeof(IPlugin))]
    // public sealed class ASFItemDropManager : IBotSteamClient, IBotCommand, IBotCardsFarmerInfo {
    public sealed class ASFItemDropManager : IBotSteamClient, IBotCommand
    {
        private static ConcurrentDictionary<Bot, ItemDropHandler> ItemDropHandlers = new ConcurrentDictionary<Bot, ItemDropHandler>();

        public string Name => "ASF Item Dropper";

        public Version Version => typeof(ASFItemDropManager).Assembly.GetName().Version ?? new Version("0");

        public void OnLoaded() => ASF.ArchiLogger.LogGenericInfo($"ASF Item Drop Plugin v{Version.ToString()} by webben | fork by Sniper677");


        public async Task<string?> OnBotCommand([NotNull] Bot bot, ulong steamID, [NotNull] string message, string[] args)
        {
            // Switch Case for seperating several argument lengths f.e.
            // 0 === no arguments give an error cause some args are required
            bot.ArchiLogger.LogGenericDebug(message: string.Join(", ", args));
            if (args.Length == 0)
            {
                bot.ArchiLogger.LogNullError(nameof(args));
                return "No Command";
            }
            
            switch (args[0].ToUpperInvariant())
            {
                // args.Length == 1base count of arguments
                // !istart bot1,bot2,bot3 218620
                //   cmd==Arg0  | arguments.length == 2 || arg[1] == bot1,bot2,bot3, arg[2] == 218620
                // !istart bot1,bot2,bot3 218620
                //   cmd  | arguments.length == 1


                // istart 218620 droplist
                case "ISTART" when args.Length == 3:
                    return await StartItemIdle(steamID, bot, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
                // istart bot1,bot2,bot3 218620 droplist
                case "ISTART" when args.Length == 4:
                    return await StartItemIdle(steamID, args[1], args[2], Utilities.GetArgsAsText(args, 3, ",")).ConfigureAwait(false);
                // istop
                case "ISTOP" when args.Length == 1:
                    return await StopItemIdle(steamID, bot).ConfigureAwait(false);
                // istop bot1,bot2,bot3
                case "ISTOP" when args.Length == 2:
                    return await StopItemIdle(steamID, args[1]).ConfigureAwait(false);
                // idrop bot1,bot2,bot appid1 item1
                case "IDROP" when args.Length == 4:
                    return await CheckItem(steamID, args[1], args[2], Utilities.GetArgsAsText(args, 3, ",")).ConfigureAwait(false);
                // idrop appid1 item1
                case "IDROP" when args.Length == 3:
                    return await CheckItem(steamID, bot, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);

                // idropdeflist
                case "IDROPDEFLIST" when args.Length == 1 :
                    return await ItemDropDefList(steamID, bot).ConfigureAwait(false);
                // idropdeflist bot1,bot2
                case "IDROPDEFLIST" when args.Length == 2:
                    return await ItemDropDefList(steamID, args[1]).ConfigureAwait(false);
                default:
                    return null;
            }
        }

        public void OnBotSteamCallbacksInit([NotNull] Bot bot, [NotNull] CallbackManager callbackManager) { }

        public IReadOnlyCollection<ClientMsgHandler> OnBotSteamHandlersInit([NotNull] Bot bot)
        {
            ItemDropHandler CurrentBotItemDropHandler = new ItemDropHandler();
            ItemDropHandlers.TryAdd(bot, CurrentBotItemDropHandler);
            return new HashSet<ClientMsgHandler> { CurrentBotItemDropHandler };
        }

        //Responses

        private static async Task<string?> StartItemIdle(ulong steamID, Bot bot, string appid, string droplist)
        {
            if (!bot.HasAccess(steamID, BotConfig.EAccess.Master))
            {
                return null;
            }

            if (!uint.TryParse(appid, out uint appId))
            {
                return bot.Commands.FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(appId)));
            }
            if (!ItemDropHandlers.TryGetValue(bot, out ItemDropHandler? ItemDropHandler))
            {
                return bot.Commands.FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(ItemDropHandlers)));
            }

            return bot.Commands.FormatBotResponse(await Task.Run<string>(() => ItemDropHandler.itemIdleingStart(bot, appId)).ConfigureAwait(false));

        }

        private static async Task<string?> StartItemIdle(ulong steamID, string botNames, string appid, string droplist)
        {
            HashSet<Bot>? bots = Bot.GetBots(botNames);
            if ((bots == null) || (bots.Count == 0))
            {
                return Commands.FormatStaticResponse(string.Format(Strings.BotNotFound, botNames));
            }

            IList<string?> results = await Utilities.InParallel(bots.Select(bot => StartItemIdle(steamID, bot, appid, droplist))).ConfigureAwait(false);

            List<string?> responses = new List<string?>(results.Where(result => !string.IsNullOrEmpty(result)));

            return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;

        }

        private static async Task<string?> StopItemIdle(ulong steamID, Bot bot)
        {
            if (!bot.HasAccess(steamID, BotConfig.EAccess.Master))
            {
                return null;
            }

            if (!ItemDropHandlers.TryGetValue(bot, out ItemDropHandler? ItemDropHandler))
            {
                return bot.Commands.FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(ItemDropHandlers)));
            }

            return bot.Commands.FormatBotResponse(await Task.Run<string>(() => ItemDropHandler.itemIdleingStop(bot)).ConfigureAwait(false));

        }
        private static async Task<string?> StopItemIdle(ulong steamID, string botNames)
        {
            HashSet<Bot>? bots = Bot.GetBots(botNames);
            if ((bots == null) || (bots.Count == 0))
            {
                return Commands.FormatStaticResponse(string.Format(Strings.BotNotFound, botNames));
            }

            IList<string?> results = await Utilities.InParallel(bots.Select(bot => StopItemIdle(steamID, bot))).ConfigureAwait(false);

            List<string?> responses = new List<string?>(results.Where(result => !string.IsNullOrEmpty(result)));

            return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;

        }

        private static async Task<string?> ItemDropDefList(ulong steamID, Bot bot)
        {
            if (!bot.HasAccess(steamID, BotConfig.EAccess.Master))
            {
                return null;
            }

            if (!ItemDropHandlers.TryGetValue(bot, out ItemDropHandler? ItemDropHandler))
            {
                return bot.Commands.FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(ItemDropHandlers)));
            }

            return bot.Commands.FormatBotResponse(await Task.Run<string>(() => ItemDropHandler.itemDropDefList(bot)).ConfigureAwait(false));

        }

        private static async Task<string?> ItemDropDefList(ulong steamID, string botNames)
        {
            HashSet<Bot>? bots = Bot.GetBots(botNames);
            if ((bots == null) || (bots.Count == 0))
            {
                return Commands.FormatStaticResponse(string.Format(Strings.BotNotFound, botNames));
            }

            IList<string?> results = await Utilities.InParallel(bots.Select(bot => ItemDropDefList(steamID, bot))).ConfigureAwait(false);

            List<string?> responses = new List<string?>(results.Where(result => !string.IsNullOrEmpty(result)));

            return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
        }


        private static async Task<string?> CheckItem(ulong steamID, Bot bot, string appid, string itemdefId)
        {
            if (!bot.HasAccess(steamID, BotConfig.EAccess.Master))
            {
                bot.ArchiLogger.LogGenericError("Bot has no access");
                return null;
            }
            if (!uint.TryParse(appid, out uint appId))
            {
                return bot.Commands.FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(appId)));
            }
            if (!uint.TryParse(itemdefId, out uint itemdefid))
            {
                return bot.Commands.FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(itemdefid)));
            }
            if (!ItemDropHandlers.TryGetValue(bot, out ItemDropHandler? ItemDropHandler))
            {
                return bot.Commands.FormatBotResponse(string.Format(Strings.ErrorIsEmpty, nameof(ItemDropHandlers)));
            }

            return bot.Commands.FormatBotResponse(await Task.Run<string>(() => ItemDropHandler.checkTime(appId, itemdefid, bot)).ConfigureAwait(false));

        }

        private static async Task<string?> CheckItem(ulong steamID, string botNames, string appid, string itemdefId)
        {
            HashSet<Bot>? bots = Bot.GetBots(botNames);

            if ((bots == null) || (bots.Count == 0))
            {
                return Commands.FormatStaticResponse(string.Format(Strings.BotNotFound, botNames));
            }

            IList<string?> results = await Utilities.InParallel(bots.Select(bot => CheckItem(steamID, bot, appid, itemdefId))).ConfigureAwait(false);

            List<string?> responses = new List<string?>(results.Where(result => !string.IsNullOrEmpty(result)));
            
            return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : "No Results";

        }

    }

}