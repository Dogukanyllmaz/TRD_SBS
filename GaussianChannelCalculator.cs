public class GaussianChannelCalculator
{
    private readonly int N;
    private readonly double mult;
    private double filt;
    private double prevFilt;
    private double hband;
    private double lband;
    private readonly double alpha;

    public double PreviousFilt => prevFilt;

    public GaussianChannelCalculator(int N, double mult)
    {
        this.N = N;
        this.mult = mult;
        this.alpha = 2.0 / (N + 1); // EMA için alpha
        filt = 0;
        prevFilt = 0;
        hband = 0;
        lband = 0;
    }

    public (double, double, double) Update(double price, double trueRange)
    {
        prevFilt = filt;
        filt = filt == 0 ? price : price * alpha + filt * (1 - alpha); // EMA hesaplama
        hband = filt + mult * trueRange;
        lband = filt - mult * trueRange;
        return (filt, hband, lband);
    }
}