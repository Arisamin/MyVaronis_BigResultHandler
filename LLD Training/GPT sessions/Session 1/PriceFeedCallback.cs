public class PriceFeedCallback : IPriceFeedCallback
{
    public PriceFeedCallback()
    {
    }

    public event Action<string, decimal> PriceUpdated;

    public void OnPriceUpdate(string symbol, decimal price)
    {
        PriceUpdated?.Invoke(symbol, price);
        // Additional logic to handle the price update can be added here
    }
}