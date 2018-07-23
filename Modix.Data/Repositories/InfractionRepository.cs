﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

using Nito.AsyncEx;

using Modix.Data.Models;
using Modix.Data.Models.Moderation;
using Modix.Data.Utilities;

namespace Modix.Data.Repositories
{
    /// <inheritdoc />
    public class InfractionRepository : RepositoryBase, IInfractionRepository
    {
        /// <summary>
        /// Creates a new <see cref="InfractionRepository"/>.
        /// See <see cref="RepositoryBase(ModixContext)"/> for details.
        /// </summary>
        public InfractionRepository(ModixContext modixContext)
            : base(modixContext) { }

        /// <inheritdoc />
        public async Task<long?> TryCreateAsync(InfractionCreationData data, InfractionSearchCriteria criteria = null)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            using (await _createLock.LockAsync())
            {
                if((criteria != null) && await ModixContext.Infractions.AsNoTracking()
                    .FilterInfractionsBy(criteria).AnyAsync())
                {
                    return null;
                }

                var entity = data.ToEntity();

                await ModixContext.Infractions.AddAsync(entity);
                await ModixContext.SaveChangesAsync();

                entity.CreateAction.InfractionId = entity.Id;
                await ModixContext.SaveChangesAsync();

                return entity.Id;
            }
        }

        /// <inheritdoc />
        public Task<InfractionSummary> ReadAsync(long infractionId)
            => ModixContext.Infractions.AsNoTracking()
                .Where(x => x.Id == infractionId)
                .Select(InfractionSummary.FromEntityProjection)
                .FirstOrDefaultAsync();

        /// <inheritdoc />
        public async Task<IReadOnlyCollection<long>> SearchIdsAsync(InfractionSearchCriteria searchCriteria)
            => await ModixContext.Infractions.AsNoTracking()
                .FilterInfractionsBy(searchCriteria)
                .Select(x => x.Id)
                .ToArrayAsync();

        /// <inheritdoc />
        public async Task<IReadOnlyCollection<InfractionSummary>> SearchSummariesAsync(InfractionSearchCriteria searchCriteria, IEnumerable<SortingCriteria> sortingCriteria = null)
            => await ModixContext.Infractions.AsNoTracking()
                .FilterInfractionsBy(searchCriteria)
                .Select(InfractionSummary.FromEntityProjection)
                .SortBy(sortingCriteria, InfractionSummary.SortablePropertyMap)
                .ToArrayAsync();

        /// <inheritdoc />
        public async Task<RecordsPage<InfractionSummary>> SearchSummariesPagedAsync(InfractionSearchCriteria searchCriteria, IEnumerable<SortingCriteria> sortingCriteria, PagingCriteria pagingCriteria)
        {
            var sourceQuery = ModixContext.Infractions.AsNoTracking();

            var filteredQuery = sourceQuery
                .FilterInfractionsBy(searchCriteria);

            var pagedQuery = filteredQuery
                .Select(InfractionSummary.FromEntityProjection)
                .SortBy(sortingCriteria, InfractionSummary.SortablePropertyMap)
                // Always sort by Id last, otherwise ordering of records with matching fields is not guaranteed by the DB
                .OrderThenBy(x => x.Id, SortDirection.Ascending)
                .PageBy(pagingCriteria);

            return new RecordsPage<InfractionSummary>()
            {
                TotalRecordCount = await sourceQuery.LongCountAsync(),
                FilteredRecordCount = await filteredQuery.LongCountAsync(),
                Records = await pagedQuery.ToArrayAsync()
            };
        }

        /// <inheritdoc />
        public async Task<bool> TryRescindAsync(long infractionId, ulong rescindedById)
        {
            var longRescindedById = (long)rescindedById;

            var entity = await ModixContext.Infractions
                .Where(x => x.Id == infractionId)
                .FirstOrDefaultAsync();

            if ((entity == null) || (entity.RescindActionId != null))
                return false;

            entity.RescindAction = new ModerationActionEntity()
            {
                Type = ModerationActionType.InfractionRescinded,
                Created = DateTimeOffset.Now,
                CreatedById = longRescindedById,
                InfractionId = entity.Id
            };
            await ModixContext.SaveChangesAsync();

            return true;
        }

        /// <inheritdoc />
        public async Task<bool> TryDeleteAsync(long infractionId, ulong deletedById)
        {
            var longDeletedById = (long)deletedById;

            var entity = await ModixContext.Infractions
                .Where(x => x.Id == infractionId)
                .FirstOrDefaultAsync();

            if ((entity == null) || (entity.DeleteActionId != null))
                return false;

            entity.DeleteAction = new ModerationActionEntity()
            {
                Type = ModerationActionType.InfractionDeleted,
                Created = DateTimeOffset.Now,
                CreatedById = longDeletedById,
                InfractionId = entity.Id
            };
            await ModixContext.SaveChangesAsync();

            return true;
        }

        private static readonly AsyncLock _createLock
            = new AsyncLock();
    }

    internal static class InfractionQueryableExtensions
    {
        public static IQueryable<InfractionEntity> FilterInfractionsBy(this IQueryable<InfractionEntity> query, InfractionSearchCriteria criteria)
        {
            var longGuildId = (long?)criteria?.GuildId;
            var longSubjectId = (long?)criteria?.SubjectId;
            var longCreatedById = (long?)criteria?.CreatedById;

            return query
                .FilterBy(
                    x => x.GuildId == longGuildId,
                    longGuildId != null)
                .FilterBy(
                    x => criteria.Types.Contains(x.Type),
                    criteria?.Types?.Any() ?? false)
                .FilterBy(
                    x => x.SubjectId == longSubjectId,
                    longSubjectId != null)
                .FilterBy(
                    x => x.CreateAction.Created >= criteria.CreatedRange.Value.From,
                    criteria?.CreatedRange?.From != null)
                .FilterBy(
                    x => x.CreateAction.Created <= criteria.CreatedRange.Value.To,
                    criteria?.CreatedRange?.To != null)
                .FilterBy(
                    x => x.CreateAction.CreatedById == longCreatedById,
                    longCreatedById != null)
                .FilterBy(
                    x => (x.RescindActionId != null) == criteria.IsRescinded,
                    criteria?.IsRescinded != null)
                .FilterBy(
                    x => (x.DeleteActionId != null) == criteria.IsDeleted,
                    criteria?.IsDeleted != null)
                .FilterBy(
                    x => (x.Duration != null) && (x.CreateAction.Created + x.Duration.Value) >= criteria.CreatedRange.Value.From,
                    criteria?.ExpiresRange?.From != null)
                .FilterBy(
                    x => (x.Duration != null) && (x.CreateAction.Created + x.Duration.Value) >= criteria.CreatedRange.Value.To,
                    criteria?.ExpiresRange?.To != null);
        }
    }
}