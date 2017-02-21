﻿using System.Text;
using System.Threading.Tasks;
using Discord;
using MongoDB.Driver;
using Monk.Data.Repositories;

namespace Modix.Services.Ban
{
    public class BanService
    {
        BanRepository repository = new BanRepository();

        public async Task BanAsync(ulong userId, ulong creatorId, ulong guildId, string reason)
        {
            var ban = new Data.Models.Ban
            {
                CreatorId = creatorId,
                UserId = userId,
                Reason = reason,
                GuildId = guildId,
            };

            await repository.InsertAsync(ban);
        }

        public async Task UnbanAsync(ulong guildId, ulong userId)
        {
            var ban = repository.GetOne(x => x.GuildId == guildId && x.UserId == userId);
            ban.Active = false;
            await repository.Update(ban.Id, ban);
        }

        public async Task<string> GetAllBans(IGuildUser user)
        {
            var bans = await repository.GetAllAsync(x => x.UserId == user.Id);
            var sb = new StringBuilder();

            await bans.ForEachAsync(ban => sb.AppendLine($"{ban.Reason} | Active: {ban.Active}"));

            return sb.ToString();
        }
    }
}