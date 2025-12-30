using System;
using System.Reactive.Subjects;
using System.Reactive.Concurrency;

// Example implementation of ResultHandler using Rx Subject for message handling
public class ResultHandler : IDisposable
{
	private readonly Subject<HeaderMessage> headerSubject = new();
	private readonly Subject<PayloadMessage> payloadSubject = new();
	private readonly IDisposable headerSubscription;
	private readonly IDisposable payloadSubscription;
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

		headerQueueConsumer.OnMessageReceived += msg => headerSubject.OnNext(msg);
		payloadQueueConsumer.OnMessageReceived += msg => payloadSubject.OnNext(msg);

		headerSubscription = headerSubject
			.ObserveOn(TaskPoolScheduler.Default)
			.Subscribe(HandleHeaderMessage, OnError, OnCompleted);

		payloadSubscription = payloadSubject
			.ObserveOn(TaskPoolScheduler.Default)
			.Subscribe(HandlePayloadMessage, OnError, OnCompleted);
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

	private void OnError(Exception ex)
	{
		// Handle errors (logging, alerting, etc.)
	}

	private void OnCompleted()
	{
		// Cleanup logic if needed
	}

	public void Dispose()
	{
		headerSubscription.Dispose();
		payloadSubscription.Dispose();
		headerSubject.Dispose();
		payloadSubject.Dispose();
	}
}

// Placeholder interfaces and message types for illustration
public interface IQueueConsumer { event Action<HeaderMessage> OnMessageReceived; }
public interface IAzureStorageService { }
public interface IKVStoreService { }
public class HeaderMessage { public byte[] Body { get; set; } }
public class PayloadMessage { public byte[] Body { get; set; } public System.IO.Stream GetPayloadStream() => null; }