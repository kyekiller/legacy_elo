﻿using Discord;
using Discord.Commands;
using ELO.Models;
using ELO.Services;
using RavenBOT.Common;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace ELO.Modules
{
    [RavenRequireContext(ContextType.Guild)]
    [Preconditions.RequirePermission(PermissionLevel.ELOAdmin)]
    public class CompetitionSetup : ReactiveBase
    {
        public PremiumService Premium { get; }

        public CompetitionSetup(PremiumService premium)
        {
            Premium = premium;
        }

        [Command("SetPrefix", RunMode = RunMode.Sync)]
        [Summary("Set the server's prefix")]
        public async Task SetPrefixAsync([Remainder]string prefix = null)
        {
            using (var db = new Database())
            {
                var comp = db.GetOrCreateCompetition(Context.Guild.Id);
                comp.Prefix = prefix;
                db.Update(comp);
                db.SaveChanges();
                await SimpleEmbedAsync($"Prefix has been set to `{prefix ?? "Default"}`");
            }
        }

        [Command("ClaimPremium", RunMode = RunMode.Sync)]
        [Summary("Claim a patreon premium subscription")]
        public async Task ClaimPremiumAsync()
        {
            await Premium.Claim(Context);
        }

        [Command("RedeemLegacyToken", RunMode = RunMode.Sync)]
        [Summary("Redeem a 16 digit token for the old version of ELO")]
        public async Task RedeemLegacyTokenAsync([Remainder]string token = null)
        {
            if (token == null)
            {
                await SimpleEmbedAsync("This is used to redeem tokens that were created using the old ELO version.", Color.Blue);
                return;
            }

            using (var db = new Database())
            {
                var legacy = db.LegacyTokens.Find(token);
                if (legacy == null)
                {
                    await SimpleEmbedAsync($"Invalid token provided, if you believe this is a mistake please contact support at: {Premium.PremiumConfig.ServerInvite}", Color.Red);
                }
                else
                {
                    var guild = db.GetOrCreateCompetition(Context.Guild.Id);
                    if (guild.LegacyPremiumExpiry == null)
                    {
                        guild.LegacyPremiumExpiry = DateTime.UtcNow + TimeSpan.FromDays(legacy.Days);
                    }
                    else
                    {
                        if (guild.LegacyPremiumExpiry < DateTime.UtcNow)
                        {
                            guild.LegacyPremiumExpiry = DateTime.UtcNow + TimeSpan.FromDays(legacy.Days);
                        }
                        else
                        {
                            guild.LegacyPremiumExpiry += TimeSpan.FromDays(legacy.Days);
                        }
                    }
                    db.Remove(legacy);
                    db.Update(guild);
                    db.SaveChanges();
                    await SimpleEmbedAsync("Token redeemed.", Color.Green);

                }
            }
        }

        [Command("LegacyExpiration", RunMode = RunMode.Sync)]
        [Summary("Displays the expiry date of any legacy subscription")]
        public async Task LegacyExpirationAsync()
        {
            using (var db = new Database())
            {
                var guild = db.GetOrCreateCompetition(Context.Guild.Id);
                if (guild.LegacyPremiumExpiry != null)
                {
                    if (guild.LegacyPremiumExpiry.Value > DateTime.UtcNow)
                    {
                        await SimpleEmbedAsync($"Expires on: {guild.LegacyPremiumExpiry.Value.ToString("dd MMM yyyy")} {guild.LegacyPremiumExpiry.Value.ToShortTimeString()}\nRemaining: {RavenBOT.Common.Extensions.GetReadableLength(guild.LegacyPremiumExpiry.Value - DateTime.UtcNow)}", Color.Blue);
                    }
                    else
                    {
                        await SimpleEmbedAsync("Legacy premium has already expired.", Color.Red);
                    }
                }
                else
                {
                    await SimpleEmbedAsync("This server does not have a legacy premium subscription.", Color.Red);
                }
            }

        }

        [Command("RegistrationLimit", RunMode = RunMode.Async)]
        [Summary("Displays the maximum amount of registrations for the server")]
        public async Task GetRegisterLimit()
        {
            await SimpleEmbedAsync($"Current registration limit is a maximum of: {Premium.GetRegistrationLimit(Context.Guild.Id)}", Color.Blue);
        }

        [Command("CompetitionInfo", RunMode = RunMode.Async)]
        [Alias("CompetitionSettings", "GameSettings")]
        [Summary("Displays information about the current servers competition settings")]
        public async Task CompetitionInfo()
        {
            using (var db = new Database())
            {
                var comp = db.GetOrCreateCompetition(Context.Guild.Id);
                var embed = new EmbedBuilder
                {
                    Color = Color.Blue
                };
                embed.AddField("Roles",
                            $"**Register Role:** {(comp.RegisteredRankId == 0 ? "N/A" : MentionUtils.MentionRole(comp.RegisteredRankId))}\n" +
                            $"**Admin Role:** {(comp.AdminRole == 0 ? "N/A" : MentionUtils.MentionRole(comp.AdminRole.Value))}\n" +
                            $"**Moderator Role:** {(comp.ModeratorRole == 0 ? "N/A" : MentionUtils.MentionRole(comp.ModeratorRole.Value))}");
                embed.AddField("Options",
                            $"**Allow Multiqueuing:** {comp.AllowMultiQueueing}\n" +
                            $"**Allow Negative Score:** {comp.AllowNegativeScore}\n" +
                            $"**Update Nicknames:** {comp.UpdateNames}\n" +
                            $"**Allow Self Rename:** {comp.AllowSelfRename}\n" +
                            $"**Allow Re-registering:** {comp.AllowReRegister}\n" +
                            $"**Requeue Delay:** {(comp.RequeueDelay.HasValue ? comp.RequeueDelay.Value.GetReadableLength() : "None")}");
                //embed.AddField("Stats",
                //            $"**Registered User Count:** {comp.RegistrationCount}\n" +
                //            $"**Manual Game Count:** {comp.ManualGameCounter}");
                embed.AddField("Formatting", $"**Nickname Format:** {comp.NameFormat}\n" +
                            $"**Registration Message:** {comp.RegisterMessageTemplate}");
                embed.AddField("Rank Info",
                $"**Default Loss Amount:** -{comp.DefaultLossModifier}\n" +
                $"**Default Win Amount:** +{comp.DefaultWinModifier}\n" +
                $"For rank info use the `ranks` command");
                await ReplyAsync(embed);
            }
        }

        [Command("SetRegisterRole", RunMode = RunMode.Sync)]
        [Alias("Set RegisterRole", "RegisterRole")]
        [Summary("Sets or displays the current register role")]
        public async Task SetRegisterRole([Remainder] IRole role = null)
        {
            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);
                if (role == null)
                {
                    if (competition.RegisteredRankId != 0)
                    {
                        var gRole = Context.Guild.GetRole(competition.RegisteredRankId);
                        if (gRole == null)
                        {
                            //Rank previously set but can no longer be found (deleted)
                            //May as well reset it.
                            competition.RegisteredRankId = 0;
                            db.Update(competition);
                            db.SaveChanges();
                            await SimpleEmbedAsync("Register role had previously been set but can no longer be found in the server. It has been reset.", Color.DarkBlue);
                        }
                        else
                        {
                            await SimpleEmbedAsync($"Current register role is: {gRole.Mention}", Color.Blue);
                        }
                    }
                    else
                    {
                        //var serverPrefix = Prefix.GetPrefix(Context.Guild.Id) ?? Prefix.DefaultPrefix;
                        await SimpleEmbedAsync($"There is no register role set. You can set one with `SetRegisterRole @role` or `SetRegisterRole rolename`", Color.Blue);
                    }

                    return;
                }

                competition.RegisteredRankId = role.Id;
                db.Update(competition);
                db.SaveChanges();
                await SimpleEmbedAsync($"Register role set to {role.Mention}", Color.Green);
            }
        }

        [Command("SetRegisterMessage", RunMode = RunMode.Sync)]
        [Alias("Set RegisterMessage")]
        [Summary("Sets the message shown to users when they register")]
        public async Task SetRegisterMessageAsync([Remainder] string message = null)
        {
            using (var db = new Database())
            {
                if (message == null)
                {
                    message = "You have registered as `{name}`, all roles/name updates have been applied if applicable.";
                }
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);
                competition.RegisterMessageTemplate = message;
                var testProfile = new Player(0, 0, "Player");
                testProfile.Wins = 5;
                testProfile.Losses = 2;
                testProfile.Draws = 1;
                testProfile.Points = 600;
                var exampleRegisterMessage = competition.FormatRegisterMessage(testProfile);

                db.Update(competition);
                db.SaveChanges();
                await SimpleEmbedAsync($"Register Message set.\nExample:\n{exampleRegisterMessage}", Color.Green);
            }
        }

        [Command("RegisterMessage", RunMode = RunMode.Async)]
        [Summary("Displays the current register message for the server")]
        public async Task ShowRegisterMessageAsync()
        {
            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);
                var testProfile = new Player(0, 0, "Player");
                testProfile.Wins = 5;
                testProfile.Losses = 2;
                testProfile.Draws = 1;
                testProfile.Points = 600;

                db.Update(competition);
                db.SaveChanges();
                var response = new EmbedBuilder
                {
                    Color = Color.Blue
                };

                if (!string.IsNullOrWhiteSpace(competition.RegisterMessageTemplate))
                {
                    response.AddField("Unformatted Message", competition.RegisterMessageTemplate);
                    response.AddField("Example Message", competition.FormatRegisterMessage(testProfile));
                    await ReplyAsync(response);
                    return;
                }

                await SimpleEmbedAsync($"This server does not have a register message set.", Color.DarkBlue);
            }
        }

        [Command("RegisterMessageFormats", RunMode = RunMode.Async)]
        [Alias("RegisterFormats")]
        [Summary("Shows replacements that can be used in the register message")]
        public async Task ShowRegistrationFormatsAsync()
        {
            var response = "**Register Message Formats**\n" + // Use Title
                "{score} - Total points\n" +
                "{name} - Registration name\n" +
                "{wins} - Total wins\n" +
                "{draws} - Total draws\n" +
                "{losses} - Total losses\n" +
                "{games} - Games played\n\n" +
                "Example:\n" +
                "`RegisterMessageFormats Thank you for registering {name}` `Thank you for registering Player`\n" +
                "NOTE: Format is limited to 1024 characters long";

            await SimpleEmbedAsync(response, Color.Blue);
        }

        [Command("SetNicknameFormat", RunMode = RunMode.Sync)]
        [Alias("Set NicknameFormat", "NicknameFormat", "NameFormat", "SetNameFormat")]
        [Summary("Sets how user nicknames are formatted")]
        public async Task SetNicknameFormatAsync([Remainder] string format)
        {
            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);
                competition.NameFormat = format;
                var testProfile = new Player(0, 0, "Player");
                testProfile.Wins = 5;
                testProfile.Losses = 2;
                testProfile.Draws = 1;
                testProfile.Points = 600;
                var exampleNick = competition.GetNickname(testProfile);

                db.Update(competition);
                db.SaveChanges();
                await SimpleEmbedAsync($"Nickname Format set.\nExample: `{exampleNick}`", Color.Green);
            }
        }

        [Command("NicknameFormats", RunMode = RunMode.Async)]
        [Alias("NameFormats")]
        [Summary("Shows replacements that can be used in the user nickname formats")]
        public async Task ShowNicknameFormatsAsync()
        {
            var response = "**NickNameFormats**\n" + // Use Title
                "{score} - Total points\n" +
                "{name} - Registration name\n" +
                "{wins} - Total wins\n" +
                "{draws} - Total draws\n" +
                "{losses} - Total losses\n" +
                "{games} - Games played\n\n" +
                "Examples:\n" +
                "`SetNicknameFormat {score} - {name}` `1000 - Player`\n" +
                "`SetNicknameFormat [{wins}] {name}` `[5] Player`\n" +
                "NOTE: Nicknames are limited to 32 characters long on discord";

            await SimpleEmbedAsync(response, Color.Blue);
        }

        [Command("AddRank", RunMode = RunMode.Sync)]
        [Alias("Add Rank", "UpdateRank")]
        [Summary("Adds a new rank with the specified amount of points")]
        public async Task AddRank(IRole role, int points)
        {
            using (var db = new Database())
            {
                var oldRank = db.Ranks.Find(role.Id);
                var newRank = new Rank
                {
                    RoleId = role.Id,
                    GuildId = Context.Guild.Id,
                    Points = points
                };
                if (oldRank != null)
                {
                    oldRank = newRank;
                    oldRank.WinModifier = null;
                    oldRank.LossModifier = null;
                    db.Update(oldRank);
                    db.SaveChanges();
                }
                else
                {
                    db.Ranks.Add(newRank);
                    db.SaveChanges();
                }
                await SimpleEmbedAsync("Rank added, if you wish to change the win/loss point values, use the `RankWinModifier` and `RankLossModifier` commands.", Color.Green);
            }
        }

        [Command("AddRank", RunMode = RunMode.Sync)]
        [Alias("Add Rank", "UpdateRank")]
        [Summary("Adds a new rank with the specified amount of points and win/loss modifiers")]
        public async Task AddRank(IRole role, int points, int win, int lose)
        {
            using (var db = new Database())
            {
                var oldRank = db.Ranks.Find(role.Id);
                var newRank = new Rank
                {
                    RoleId = role.Id,
                    GuildId = Context.Guild.Id,
                    Points = points,
                    WinModifier = win,
                    LossModifier = lose
                };
                if (oldRank != null)
                {
                    oldRank = newRank;
                    db.Update(oldRank);
                    db.SaveChanges();
                }
                else
                {
                    db.Ranks.Add(newRank);
                    db.SaveChanges();
                }
                await SimpleEmbedAsync($"Rank added.\n**Required Points:** {newRank.Points}\n**Win Modifier:** +{newRank.WinModifier}\n**Loss Modifier:** -{newRank.LossModifier}", Color.Green);
            }
        }


        [Command("AddRank", RunMode = RunMode.Sync)]
        [Alias("Add Rank", "UpdateRank")]
        [Summary("Adds a new rank with the specified amount of points and win/loss modifiers")]
        public async Task AddRank(int points, IRole role, int win, int lose)
        {
            await AddRank(role, points, win, lose);
        }

        [Command("AddRank", RunMode = RunMode.Sync)]
        [Alias("Add Rank", "UpdateRank")]
        [Summary("Adds a new rank with the specified amount of points")]
        public async Task AddRank(int points, IRole role)
        {
            await AddRank(role, points);
        }

        [Command("RemoveRank", RunMode = RunMode.Sync)]
        [Alias("Remove Rank", "DelRank")]
        [Summary("Removes a rank based of the role's id")]
        public async Task RemoveRank(ulong roleId)
        {
            using (var db = new Database())
            {
                var rank = db.Ranks.Find(roleId);
                if (rank == null)
                {
                    await SimpleEmbedAsync("Invalid Rank.", Color.Red);
                    return;
                }

                db.Ranks.Remove(rank);
                db.SaveChanges();
                await SimpleEmbedAsync("Rank Removed.", Color.Green);
            }
        }

        [Command("RemoveRank", RunMode = RunMode.Sync)]
        [Alias("Remove Rank", "DelRank")]
        [Summary("Removes a rank")]
        public async Task RemoveRank(IRole role)
        {
            await RemoveRank(role.Id);
        }

        [Command("AllowNegativeScore", RunMode = RunMode.Sync)]
        [Alias("AllowNegative")]
        [Summary("Sets whether negative scores are allowed")]
        public async Task AllowNegativeAsync(bool? allowNegative = null)
        {
            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);
                if (allowNegative == null)
                {
                    await SimpleEmbedAsync($"Current Allow Negative Score Setting: {competition.AllowNegativeScore}", Color.Blue);
                    return;
                }
                competition.AllowNegativeScore = allowNegative.Value;
                db.Update(competition);
                db.SaveChanges();
                await SimpleEmbedAsync($"Allow Negative Score set to {allowNegative.Value}", Color.Green);
            }
        }

        [Command("AllowReRegister", RunMode = RunMode.Sync)]
        [Summary("Sets whether users are allowed to run the register command multiple times")]
        public async Task AllowReRegisterAsync(bool? reRegister = null)
        {
            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);
                if (reRegister == null)
                {
                    await SimpleEmbedAsync($"Current Allow re-register Setting: {competition.AllowReRegister}", Color.Blue);
                    return;
                }
                competition.AllowReRegister = reRegister.Value;
                db.Update(competition);
                db.SaveChanges();
                await SimpleEmbedAsync($"Allow re-register set to {reRegister.Value}", Color.Green);
            }
        }

        [Command("AllowSelfRename", RunMode = RunMode.Sync)]
        [Summary("Sets whether users are allowed to use the rename command")]
        public async Task AllowSelfRenameAsync(bool? selfRename = null)
        {
            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);
                if (selfRename == null)
                {
                    await SimpleEmbedAsync($"Current Allow Self Rename Setting: {competition.AllowSelfRename}", Color.Blue);
                    return;
                }
                competition.AllowSelfRename = selfRename.Value;
                db.Update(competition);
                db.SaveChanges();
                await SimpleEmbedAsync($"Allow Self Rename set to {selfRename.Value}", Color.Green);
            }
        }

        [Command("DefaultWinModifier", RunMode = RunMode.Sync)]
        [Summary("Sets the default amount of points users can earn when winning.")]
        public async Task CompWinModifier(int? amountToAdd = null)
        {
            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);

                if (!amountToAdd.HasValue)
                {
                    await SimpleEmbedAsync($"Current DefaultWinModifier Setting: {competition.DefaultWinModifier}", Color.Blue);
                    return;
                }
                competition.DefaultWinModifier = amountToAdd.Value;
                db.Update(competition);
                db.SaveChanges();
                await SimpleEmbedAsync($"Default Win Modifier set to {competition.DefaultWinModifier}", Color.Green);
            }
        }


        [Command("DefaultLossModifier", RunMode = RunMode.Sync)]
        [Summary("Sets the default amount of points users lose when the lose a game.")]
        public async Task CompLossModifier(int? amountToSubtract = null)
        {
            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);

                if (!amountToSubtract.HasValue)
                {
                    await SimpleEmbedAsync($"Current DefaultLossModifier Setting: {competition.DefaultLossModifier}", Color.Blue);
                    return;
                }
                competition.DefaultLossModifier = amountToSubtract.Value;
                db.Update(competition);
                db.SaveChanges();
                await SimpleEmbedAsync($"Default Loss Modifier set to {competition.DefaultLossModifier}", Color.Green);
            }
        }

        [Command("RankLossModifier", RunMode = RunMode.Sync)]
        [Summary("Sets the amount of points lost for a user with the specified rank.")]
        public async Task RankLossModifier(IRole role, int? amountToSubtract = null)
        {
            using (var db = new Database())
            {
                var rank = db.Ranks.Find(role.Id);
                if (rank == null)
                {
                    await SimpleEmbedAsync("Provided role is not a rank.", Color.Red);
                    return;
                }

                rank.LossModifier = amountToSubtract;
                db.Update(rank);
                db.SaveChanges();
                if (!amountToSubtract.HasValue)
                {
                    var competition = db.GetOrCreateCompetition(Context.Guild.Id);
                    await SimpleEmbedAsync($"This rank will now use the server's default loss value (-{competition.DefaultLossModifier}) when subtracting points.", Color.Blue);
                }
                else
                {
                    await SimpleEmbedAsync($"When a player with this rank loses they will lose {amountToSubtract} points", Color.Green);
                }
            }
        }

        [Command("RankWinModifier", RunMode = RunMode.Sync)]
        [Summary("Sets the amount of points lost for a user with the specified rank.")]
        public async Task RankWinModifier(IRole role, int? amountToAdd = null)
        {
            using (var db = new Database())
            {
                var rank = db.Ranks.Find(role.Id);
                if (rank == null)
                {
                    await SimpleEmbedAsync("Provided role is not a rank.", Color.Red);
                    return;
                }

                rank.WinModifier = amountToAdd;
                db.Update(rank);
                db.SaveChanges();
                if (!amountToAdd.HasValue)
                {
                    var competition = db.GetOrCreateCompetition(Context.Guild.Id);
                    await SimpleEmbedAsync($"This rank will now use the server's default win value (+{competition.DefaultWinModifier}) whenSimpleEmbedAsync adding points.", Color.Blue);
                }
                else
                {
                    await SimpleEmbedAsync($"When a player with this rank wins they will gain {amountToAdd} points", Color.Green);
                }
            }
        }

        [Command("UpdateNicknames", RunMode = RunMode.Sync)]
        [Summary("Sets whether the bot will update user nicknames.")]
        public async Task UpdateNicknames(bool? updateNicknames = null)
        {
            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);
                if (updateNicknames == null)
                {
                    await SimpleEmbedAsync($"Current Update Nicknames Setting: {competition.UpdateNames}", Color.Blue);
                    return;
                }
                competition.UpdateNames = updateNicknames.Value;
                db.Update(competition);
                db.SaveChanges();
                await SimpleEmbedAsync($"Update Nicknames set to {competition.UpdateNames}", Color.Green);
            }
        }


        [Command("CreateReactionRegistration", RunMode = RunMode.Sync)]
        [Summary("Creates a message which users can react to in order to register")]
        public async Task CreateReactAsync([Remainder]string message = null)
        {
            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);

                var response = await SimpleEmbedAsync(message);
                competition.ReactiveMessage = response.Id;
                db.Update(competition);
                db.SaveChanges();
                await response.AddReactionAsync(ReactiveMessageService.registrationConfirmEmoji);
            }
        }


        [Command("ReQueueDelay", RunMode = RunMode.Sync)]
        [Summary("Set or displays the amount of time required between joining queues.")]
        [Alias("SetRequeueDelay")]
        public async Task SetReQueueDelayAsync([Remainder]TimeSpan? delay = null)
        {
            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);
                if (delay == null)
                {
                    await SimpleEmbedAsync($"Current Requeue Delay Setting: {(competition.RequeueDelay.HasValue ? competition.RequeueDelay.Value.GetReadableLength() : "None")}", Color.Blue);
                    return;
                }

                competition.RequeueDelay = delay;
                db.Update(competition);
                db.SaveChanges();
                await SimpleEmbedAsync($"Requeue Delay Set to {competition.RequeueDelay.Value.GetReadableLength()}", Color.Green);
            }
        }

        [Command("ResetReQueueDelay", RunMode = RunMode.Sync)]
        [Summary("Removes the amount of time required between joining queues.")]
        public async Task ResetReQueueDelayAsync()
        {
            using (var db = new Database())
            {
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);

                competition.RequeueDelay = null;
                db.Update(competition);
                db.SaveChanges();
                await SimpleEmbedAsync($"Requeue Delay Removed.", Color.Green);
            }
        }
    }
}