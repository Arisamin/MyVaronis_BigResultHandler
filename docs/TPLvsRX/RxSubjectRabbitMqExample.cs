using System;
// using System.Reactive.Subjects; // no longer needed
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

// ...existing code...

// MessageProcessor using Rx Subject
using System.Reactive.Concurrency;
using System.Reactive.Subjects;
// SubjectMessageProcessor can optionally use ObserveOn to offload processing to the thread pool.
// Motivation: Using ObserveOn(TaskPoolScheduler.Default) ensures that message processing happens asynchronously
// on thread pool threads, immediately releasing the producer thread after OnNext is called. This prevents the
// producer from being blocked by slow consumers and more closely matches the behavior of TPL Dataflow's ActionBlock.
// However, unlike TPL Dataflow, Rx Subject with ObserveOn does not provide built-in backpressure or bounded buffering.
public class SubjectMessageProcessor
{
	private readonly Subject<IMessage> _subject = new Subject<IMessage>();
	private readonly bool _useObserveOn;

	public SubjectMessageProcessor(bool useObserveOn = false)
	{
		_useObserveOn = useObserveOn;
		if (_useObserveOn)
		{            
			_subject
			// Use ObserveOn to offload processing to the thread pool and release the producer thread immediately
			.ObserveOn(TaskPoolScheduler.Default)
			.Subscribe(OnProcess, OnComplete);
		}
		else
		{
			_subject.Subscribe(OnProcess, OnComplete);
		}
	}

	public void Process(IMessage msg) => _subject.OnNext(msg);
	public void Complete() => _subject.OnCompleted();

	private void OnProcess(IMessage msg)
	{
		Console.WriteLine($"[SubjectMessageProcessor] Processing: {msg.Body} on thread {Thread.CurrentThread.ManagedThreadId}");
		Thread.Sleep(300); // Simulate processing time
	}

	private void OnComplete()
	{
		Console.WriteLine($"[SubjectMessageProcessor] Completed");
	}
}
// MessageProcessor using TPL Dataflow
public class DataflowMessageProcessor
{
	private readonly ActionBlock<IMessage> _block;
	private readonly ActionBlock<IMessage> _block;

	public DataflowMessageProcessor()
	{
		_block = new ActionBlock<IMessage>(OnProcess);
	}

	public void Process(IMessage msg) => _block.Post(msg);
	public void Complete() => _block.Complete();

	private void OnProcess(IMessage msg)
	{
		Console.WriteLine($"[DataflowMessageProcessor] Processing: {msg.Body} on thread {Thread.CurrentThread.ManagedThreadId}");
		Thread.Sleep(300); // Simulate processing time
	}
}

// Handler can be parameterized to use either processor
public class RabbitMessageHandler
{
	bool _useSubjectProcessor = false;
	private readonly DataflowMessageProcessor _dataflowProcessor = null;
	private readonly SubjectMessageProcessor _subjectProcessor = null;

	public RabbitMessageHandler(bool useSubjectProcessor)