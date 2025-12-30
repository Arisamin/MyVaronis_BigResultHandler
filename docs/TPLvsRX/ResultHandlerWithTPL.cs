using System;
using System.Threading.Tasks.Dataflow;

// Example implementation of ResultHandler using TPL Dataflow for message handling
public class ResultHandler : IDisposable
{
    private readonly ActionBlock<HeaderMessage> headerBlock;
    private readonly ActionBlock<PayloadMessage> payloadBlock;
    private readonly IAzureStorageService storageService;
    private readonly IKVStoreService kvStoreService;

    public ResultHandler(
        IQueueConsumer headerQueueConsumer,
        IQueueConsumer payloadQueueConsumer,
        IAzureStorageService storageService,
        IKVStoreService kvStoreService)
    {
        this.storageService = storageService;
        this.kvStoreService = kvStoreService;

        // Configure ActionBlocks (optionally set BoundedCapacity, MaxDegreeOfParallelism, etc.)
        headerBlock = new ActionBlock<HeaderMessage>(HandleHeaderMessage);
        payloadBlock = new ActionBlock<PayloadMessage>(HandlePayloadMessage);

        headerQueueConsumer.OnMessageReceived += msg => headerBlock.Post(msg);
        payloadQueueConsumer.OnMessageReceived += msg => payloadBlock.Post(msg);
    }

    private void HandleHeaderMessage(HeaderMessage message)
    {
        // Example: Deserialize and write transaction metadata
        // var header = ProtoBuf.Serializer.Deserialize<HeaderMessageProto>(message.Body);
        // kvStoreService.WriteTransactionMetadata(header.TransactionId, header.SeriesTypes, header.SeriesCounts);
        // Log.Info($"Initialized transaction {header.TransactionId}");
    }

    private void HandlePayloadMessage(PayloadMessage message)
    {
        // Example: Deserialize, stream upload, and write blob mapping
        // var payload = ProtoBuf.Serializer.Deserialize<PayloadMessageProto>(message.Body);
        // using var dataStream = message.GetPayloadStream();
        // var blobUri = storageService.UploadBlobStream(payload.TransactionId, payload.SeriesType, payload.Ordinal, dataStream);
        // kvStoreService.WriteBlobMapping(payload.TransactionId, payload.SeriesType, payload.Ordinal, blobUri);
        // Log.Info($"Uploaded and mapped blob for transaction {payload.TransactionId}");
    }

    public void Dispose()
    {
        headerBlock.Complete();
        payloadBlock.Complete();
        // Optionally wait for completion
        // headerBlock.Completion.Wait();
        // payloadBlock.Completion.Wait();
    }
}

// Placeholder interfaces and message types for illustration
public interface IQueueConsumer { event Action<HeaderMessage> OnMessageReceived; }
public interface IAzureStorageService { }
public interface IKVStoreService { }
public class HeaderMessage { public byte[] Body { get; set; } }
public class PayloadMessage { public byte[] Body { get; set; } public System.IO.Stream GetPayloadStream() => null; }
