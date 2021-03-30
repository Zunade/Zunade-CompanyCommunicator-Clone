﻿// <copyright file="BaseRepository.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace Microsoft.Teams.Apps.CompanyCommunicator.Common.Repositories
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.Table;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Base repository for the data stored in the Azure Table Storage.
    /// </summary>
    /// <typeparam name="T">Entity class type.</typeparam>
    public class BaseRepository<T>
        where T : TableEntity, new()
    {
        private readonly string defaultPartitionKey;

        /// <summary>
        /// Initializes a new instance of the <see cref="BaseRepository{T}"/> class.
        /// </summary>
        /// <param name="logger">The logging service.</param>
        /// <param name="storageAccountConnectionString">The storage account connection string.</param>
        /// <param name="tableName">The name of the table in Azure Table Storage.</param>
        /// <param name="defaultPartitionKey">Default partition key value.</param>
        /// <param name="ensureTableExists">Flag to ensure the table is created if it doesn't exist.</param>
        public BaseRepository(
            ILogger logger,
            string storageAccountConnectionString,
            string tableName,
            string defaultPartitionKey,
            bool ensureTableExists)
        {
            this.Logger = logger;

            var storageAccount = CloudStorageAccount.Parse(storageAccountConnectionString);
            var tableClient = storageAccount.CreateCloudTableClient();
            this.Table = tableClient.GetTableReference(tableName);
            this.defaultPartitionKey = defaultPartitionKey;

            if (ensureTableExists)
            {
                this.Table.CreateIfNotExists();
            }
        }

        /// <summary>
        /// Gets cloud table instance.
        /// </summary>
        public CloudTable Table { get; }

        /// <summary>
        /// Gets the logger service.
        /// </summary>
        protected ILogger Logger { get; }

        /// <summary>
        /// Create or update an entity in the table storage.
        /// </summary>
        /// <param name="entity">Entity to be created or updated.</param>
        /// <returns>A task that represents the work queued to execute.</returns>
        public async Task CreateOrUpdateAsync(T entity)
        {
            try
            {
                var operation = TableOperation.InsertOrReplace(entity);
                await this.Table.ExecuteAsync(operation);
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Insert or merge an entity in the table storage.
        /// </summary>
        /// <param name="entity">Entity to be inserted or merged.</param>
        /// <returns>A task that represents the work queued to execute.</returns>
        public async Task InsertOrMergeAsync(T entity)
        {
            try
            {
                var operation = TableOperation.InsertOrMerge(entity);
                await this.Table.ExecuteAsync(operation);
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Delete an entity in the table storage.
        /// </summary>
        /// <param name="entity">Entity to be deleted.</param>
        /// <returns>A task that represents the work queued to execute.</returns>
        public async Task DeleteAsync(T entity)
        {
            try
            {
                var partitionKey = entity.PartitionKey;
                var rowKey = entity.RowKey;
                entity = await this.GetAsync(partitionKey, rowKey);
                if (entity == null)
                {
                    throw new KeyNotFoundException(
                        $"Not found in table storage. PartitionKey = {partitionKey}, RowKey = {rowKey}");
                }

                var operation = TableOperation.Delete(entity);
                await this.Table.ExecuteAsync(operation);
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Get an entity by the keys in the table storage.
        /// </summary>
        /// <param name="partitionKey">The partition key of the entity.</param>
        /// <param name="rowKey">The row key for the entity.</param>
        /// <returns>The entity matching the keys.</returns>
        public async Task<T> GetAsync(string partitionKey, string rowKey)
        {
            try
            {
                var operation = TableOperation.Retrieve<T>(partitionKey, rowKey);
                var result = await this.Table.ExecuteAsync(operation);
                return result.Result as T;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Get entities from the table storage in a partition with a filter.
        /// </summary>
        /// <param name="filter">Filter to the result.</param>
        /// <param name="partition">Partition key value.</param>
        /// <returns>All data entities.</returns>
        public async Task<IEnumerable<T>> GetWithFilterAsync(string filter, string partition = null)
        {
            try
            {
                var partitionKeyFilter = this.GetPartitionKeyFilter(partition);
                var combinedFilter = this.CombineFilters(filter, partitionKeyFilter);
                var query = new TableQuery<T>().Where(combinedFilter);
                var entities = await this.ExecuteQueryAsync(query);
                return entities;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Get all data entities from the table storage in a partition.
        /// </summary>
        /// <param name="partition">Partition key value.</param>
        /// <param name="count">The max number of desired entities.</param>
        /// <returns>All data entities.</returns>
        public async Task<IEnumerable<T>> GetAllAsync(string partition = null, int? count = null)
        {
            try
            {
                var partitionKeyFilter = this.GetPartitionKeyFilter(partition);
                var query = new TableQuery<T>().Where(partitionKeyFilter);
                var entities = await this.ExecuteQueryAsync(query, count);
                return entities;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Get paged data entities from the table storage in a partition.
        /// </summary>
        /// <param name="partition">Partition key value.</param>
        /// <param name="count">The max number of desired entities.</param>
        /// <returns>All data entities and continuation token.</returns>
        public async Task<(IEnumerable<T>, TableContinuationToken)> GetByCountAsync(string partition = null, int? count = null)
        {
            var partitionKeyFilter = this.GetPartitionKeyFilter(partition);
            var query = new TableQuery<T>().Where(partitionKeyFilter);
            query.TakeCount = count;
            TableQuerySegment<T> seg = await this.Table.ExecuteQuerySegmentedAsync<T>(query, null);
            return (seg.Results, seg.ContinuationToken);
        }

        /// <summary>
        /// Get data entities from the table storage in a partition using token.
        /// </summary>
        /// <param name="token">Continuation token.</param>
        /// <param name="partition">Partition key value.</param>
        /// <param name="count">The max number of desired entities.</param>
        /// <returns>All data entities.</returns>
        public async Task<(IEnumerable<T>, TableContinuationToken)> GetByTokenAsync(TableContinuationToken token, string partition = null, int? count = null)
        {
            var partitionKeyFilter = this.GetPartitionKeyFilter(partition);
            var query = new TableQuery<T>().Where(partitionKeyFilter);
            query.TakeCount = count;
            TableQuerySegment<T> seg = await this.Table.ExecuteQuerySegmentedAsync<T>(query, token);
            return (seg.Results, seg.ContinuationToken);
        }

        /// <summary>
        /// Get filtered data entities by date time from the table storage.
        /// </summary>
        /// <param name="dateTime">less than date time.</param>
        /// <returns>Filtered data entities.</returns>
        public async Task<IEnumerable<T>> GetAllLessThanDateTimeAsync(DateTime dateTime)
        {
            var filterByDate = TableQuery.GenerateFilterConditionForDate("Timestamp", QueryComparisons.LessThanOrEqual, dateTime);

            var query = new TableQuery<T>().Where(filterByDate);

            var entities = await this.ExecuteQueryAsync(query);

            return entities;
        }

        /// <summary>
        /// Get all data stream from the table storage in a partition.
        /// </summary>
        /// <param name="partition">Partition key value.</param>
        /// <param name="count">The max number of desired entities.</param>
        /// <returns>All data stream..</returns>
        public async IAsyncEnumerable<IEnumerable<T>> GetStreamsAsync(string partition = null, int? count = null)
        {
            var partitionKeyFilter = this.GetPartitionKeyFilter(partition);

            var query = new TableQuery<T>().Where(partitionKeyFilter);
            query.TakeCount = count;

            TableContinuationToken token = null;
            TableQuerySegment<T> seg = await this.Table.ExecuteQuerySegmentedAsync<T>(query, token);
            token = seg.ContinuationToken;
            yield return seg;
            while (token != null)
            {
                seg = await this.Table.ExecuteQuerySegmentedAsync<T>(query, token);
                token = seg.ContinuationToken;
                yield return seg;
            }
        }

        /// <summary>
        /// Insert or merge a batch of entities in Azure table storage.
        /// A batch can contain up to 100 entities.
        /// </summary>
        /// <param name="entities">Entities to be inserted or merged in Azure table storage.</param>
        /// <returns>A task that represents the work queued to execute.</returns>
        public async Task BatchInsertOrMergeAsync(IEnumerable<T> entities)
        {
            try
            {
                var array = entities.ToArray();
                for (var i = 0; i <= array.Length / 100; i++)
                {
                    var lowerBound = i * 100;
                    var upperBound = Math.Min(lowerBound + 99, array.Length - 1);
                    if (lowerBound > upperBound)
                    {
                        break;
                    }

                    var batchOperation = new TableBatchOperation();
                    for (var j = lowerBound; j <= upperBound; j++)
                    {
                        batchOperation.InsertOrMerge(array[j]);
                    }

                    await this.Table.ExecuteBatchAsync(batchOperation);
                }
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Insert or merge a batch of entities in Azure table storage.
        /// A batch can contain up to 100 entities.
        /// </summary>
        /// <param name="entities">Entities to be inserted or merged in Azure table storage.</param>
        /// <returns>A task that represents the work queued to execute.</returns>
        public async Task BatchDeleteAsync(IEnumerable<T> entities)
        {
            var array = entities.ToArray();
            for (var i = 0; i <= array.Length / 100; i++)
            {
                var lowerBound = i * 100;
                var upperBound = Math.Min(lowerBound + 99, array.Length - 1);
                if (lowerBound > upperBound)
                {
                    break;
                }

                var batchOperation = new TableBatchOperation();
                for (var j = lowerBound; j <= upperBound; j++)
                {
                    batchOperation.Delete(array[j]);
                }

                await this.Table.ExecuteBatchAsync(batchOperation);
            }
        }

        /// <summary>
        /// Get a filter that filters in the entities matching the incoming row keys.
        /// </summary>
        /// <param name="rowKeys">Row keys.</param>
        /// <returns>A filter that filters in the entities matching the incoming row keys.</returns>
        protected string GetRowKeysFilter(IEnumerable<string> rowKeys)
        {
            try
            {
                var rowKeysFilter = string.Empty;
                foreach (var rowKey in rowKeys)
                {
                    var singleRowKeyFilter = TableQuery.GenerateFilterCondition(
                        nameof(TableEntity.RowKey),
                        QueryComparisons.Equal,
                        rowKey);

                    if (string.IsNullOrWhiteSpace(rowKeysFilter))
                    {
                        rowKeysFilter = singleRowKeyFilter;
                    }
                    else
                    {
                        rowKeysFilter = TableQuery.CombineFilters(rowKeysFilter, TableOperators.Or, singleRowKeyFilter);
                    }
                }

                return rowKeysFilter;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, ex.Message);
                throw;
            }
        }

        private string CombineFilters(string filter1, string filter2)
        {
            if (string.IsNullOrWhiteSpace(filter1) && string.IsNullOrWhiteSpace(filter2))
            {
                return string.Empty;
            }
            else if (string.IsNullOrWhiteSpace(filter1))
            {
                return filter2;
            }
            else if (string.IsNullOrWhiteSpace(filter2))
            {
                return filter1;
            }

            return TableQuery.CombineFilters(filter1, TableOperators.And, filter2);
        }

        private string GetPartitionKeyFilter(string partition)
        {
            var filter = TableQuery.GenerateFilterCondition(
                nameof(TableEntity.PartitionKey),
                QueryComparisons.Equal,
                string.IsNullOrWhiteSpace(partition) ? this.defaultPartitionKey : partition);
            return filter;
        }

        private async Task<IList<T>> ExecuteQueryAsync(
            TableQuery<T> query,
            int? count = null,
            CancellationToken ct = default)
        {
            query.TakeCount = count;

            try
            {
                var result = new List<T>();
                TableContinuationToken token = null;

                do
                {
                    TableQuerySegment<T> seg = await this.Table.ExecuteQuerySegmentedAsync<T>(query, token);
                    token = seg.ContinuationToken;
                    result.AddRange(seg);
                }
                while (token != null
                    && !ct.IsCancellationRequested
                    && (count == null || result.Count < count.Value));

                return result;
            }
            catch (StorageException e)
            {
                Console.WriteLine(e.Message);
                throw;
            }
        }
    }
}