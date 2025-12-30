using System;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

// The event producer (publishes events to the subject)
class EventProducer
{
    private readonly ISubject<string> _subject;
    public EventProducer(ISubject<string> subject) => _subject = subject;

    public void Start()
    {
        Task.Run(() =>
        {
            Thread.Sleep(500);
            _subject.OnNext("Event 1");
            Thread.Sleep(1000);
            _subject.OnNext("Event 2");
            Thread.Sleep(700);
            _subject.OnNext("Event 3");
            _subject.OnCompleted();
        });
    }
}

// The subscriber (handles events from the subject)
class EventSubscriber
{
    private readonly string _name;
    public EventSubscriber(string name) => _name = name;

    public void SubscribeTo(ISubject<string> subject)
    {
        subject.Subscribe(
            OnEventReceived,
            OnCompleted
        );
    }

    // Called when an event is received from the subject
    private void OnEventReceived(string msg)
    {
        Console.WriteLine($"[{_name}] Received: {msg}");
    }

    // Called when the subject completes
    private void OnCompleted()
    {
        Console.WriteLine($"[{_name}] Completed");
    }
}

class Program
{
    static void Main()
    {
        var subject = new Subject<string>();

        // Create and start the producer
        var producer = new EventProducer(subject);
        producer.Start();

        // Create and subscribe the first subscriber immediately
        var subscriber1 = new EventSubscriber("Subscriber 1");
        subscriber1.SubscribeTo(subject);

        // Create and subscribe the second subscriber later
        Task.Run(() =>
        {
            Thread.Sleep(1200);
            var subscriber2 = new EventSubscriber("Subscriber 2");
            subscriber2.SubscribeTo(subject);
        });

        Thread.Sleep(3000); // Keep main thread alive to see all output
    }
}
