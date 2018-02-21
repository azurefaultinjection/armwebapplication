﻿using AzureChaos.Core.Models;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AzureChaos.Core.Providers
{
    /// <summary>The storage account provider.</summary>
    /// Creates the storage account if not any for the given storage account name in the config.
    /// Create the table client for the given storage account.
    public static class StorageAccountProvider
    {
        /// <summary>Default format for the storage connection string.</summary>
        private const string ConnectionStringFormat = "DefaultEndpointsProtocol=https;AccountName={0};AccountKey={1};EndpointSuffix=core.windows.net";
        private static readonly CloudStorageAccount storageAccount;

        static StorageAccountProvider()
        {
            storageAccount = CloudStorageAccount.Parse(
                                string.Format(ConnectionStringFormat,
                                              AzureClient.AzureSettings.Client.StorageAccountName,
                                              "b7yYCgyI9jg5fsRCr08tHzeic0CT5pelmpb2ZMcBaZKWhe8HdycOOs9r3luB2xygOwrbxFBnxLpysjzURKkQLQ=="));
        }

        public static CloudTable CreateOrGetTable(string tableName)
        {
            var tableClient = storageAccount.CreateCloudTableClient() ?? throw new ArgumentNullException($"storageAccount.CreateCloudTableClient()");

            // Retrieve a reference to the table.
            var table = tableClient.GetTableReference(tableName);

            // Create the table if it doesn't exist.
            table.CreateIfNotExists();

            return table;
        }

        public static void InsertOrMerge<T>(T entity, string tableName) where T : ITableEntity
        {
            var table = CreateOrGetTable(tableName);
            if (table == null)
            {
                return;
            }

            var tableOperation = TableOperation.InsertOrMerge(entity);
            table.Execute(tableOperation);
        }

        public static IEnumerable<T> GetEntities<T>(TableQuery<T> query, string tableName) where T : ITableEntity, new()
        {
            if (query == null)
            {
                return null;
            }

            var table = CreateOrGetTable(tableName);
            if (table == null)
            {
                return null;
            }

            TableContinuationToken continuationToken = null;
            IEnumerable<T> results = null;
            do
            {
                var token = continuationToken;
                var result = table.ExecuteQuerySegmented(query, token);
                results = results != null ? results.Concat(result.Results) : result;

                continuationToken = result.ContinuationToken;
            } while (continuationToken != null);

            return results;
        }
    }
}