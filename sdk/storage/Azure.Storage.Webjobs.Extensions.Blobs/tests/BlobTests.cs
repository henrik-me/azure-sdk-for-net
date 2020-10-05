﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Azure.Storage.Queues;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Azure.WebJobs.Extensions.Storage.Common;
using NUnit.Framework;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    public class BlobTests
    {
        private const string TriggerQueueName = "input-blobtests";
        private const string ContainerName = "container-blobtests";
        private const string BlobName = "blob";
        private const string BlobPath = ContainerName + "/" + BlobName;

        private StorageAccount account;

        [SetUp]
        public void SetUp()
        {
            account = AzuriteNUnitFixture.Instance.GetAccount();
            account.CreateBlobServiceClient().GetBlobContainerClient(ContainerName).DeleteIfExists();
            account.CreateQueueServiceClient().GetQueueClient(TriggerQueueName).DeleteIfExists();
        }

        [Test]
        public async Task Blob_IfBoundToCloudBlockBlob_BindsAndCreatesContainerButNotBlob()
        {
            // Act
            var prog = new BindToCloudBlockBlobProgram();
            IHost host = new HostBuilder()
                .ConfigureDefaultTestHost<BindToCloudBlockBlobProgram>(prog, builder =>
                {
                    builder.AddAzureStorageBlobs().AddAzureStorageQueues()
                    .UseStorage(account);
                })
                .Build();

            var jobHost = host.GetJobHost<BindToCloudBlockBlobProgram>();
            await jobHost.CallAsync(nameof(BindToCloudBlockBlobProgram.Run));

            var result = prog.Result;

            // Assert
            Assert.NotNull(result);
            Assert.AreEqual(BlobName, result.Name);
            Assert.NotNull(result.BlobContainerName);
            Assert.AreEqual(ContainerName, result.BlobContainerName);
            var container = GetContainerReference(account, ContainerName);
            Assert.True(await container.ExistsAsync());
            var blob = container.GetBlockBlobClient(BlobName);
            Assert.False(await blob.ExistsAsync());
        }

        [Test]
        public async Task Blob_IfBoundToTextWriter_CreatesBlob()
        {
            // Arrange
            const string expectedContent = "message";
            QueueClient triggerQueue = CreateQueue(account, TriggerQueueName);
            await triggerQueue.SendMessageAsync(expectedContent);

            // Act
            await RunTrigger(account, typeof(BindToTextWriterProgram));

            // Assert
            var container = GetContainerReference(account, ContainerName);
            Assert.True(await container.ExistsAsync());
            var blob = container.GetBlockBlobClient(BlobName);
            Assert.True(await blob.ExistsAsync());
            string content = await blob.DownloadTextAsync();
            Assert.AreEqual(expectedContent, content);
        }

        private static QueueClient CreateQueue(StorageAccount account, string queueName)
        {
            var client = account.CreateQueueServiceClient();
            var queue = client.GetQueueClient(queueName);
            queue.CreateIfNotExists();
            return queue;
        }

        private static BlobContainerClient GetContainerReference(StorageAccount account, string containerName)
        {
            var client = account.CreateBlobServiceClient();
            return client.GetBlobContainerClient(ContainerName);
        }

        private static async Task RunTrigger(StorageAccount account, Type programType)
        {
            await FunctionalTest.RunTriggerAsync(account, programType);
        }

        private class BindToCloudBlockBlobProgram
        {
            public BlockBlobClient Result { get; set; }

            public void Run(
                [Blob(BlobPath)] BlockBlobClient blob)
            {
                this.Result = blob;
            }
        }

        private class BindToTextWriterProgram
        {
            public static void Run([QueueTrigger(TriggerQueueName)] string message,
                [Blob(BlobPath)] TextWriter blob)
            {
                blob.Write(message);
                blob.Flush();
            }
        }
    }
}
