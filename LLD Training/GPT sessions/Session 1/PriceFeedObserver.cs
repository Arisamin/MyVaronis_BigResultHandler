using System.Reactive.Subjects;

public class PriceFeedCallback : IPriceFeedCallback
{
    private readonly Subject<(string Symbol, decimal Price)> _priceUpdates;

    public PriceFeedCallback()
    {
        _priceUpdates = new Subject<(string Symbol, decimal Price)>();
    }

    public void OnPriceUpdate(string symbol, decimal price)
    {
        _priceUpdates.OnNext((symbol, price));
        // Additional logic to handle the price update can be added here
    }

    public void Subscribe(IObserver<(string Symbol, decimal Price)> observer)
    {
        _priceUpdates.Subscribe(observer);
    }
}