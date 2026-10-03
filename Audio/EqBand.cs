namespace SpigenAudioCTRL.Audio
{
    public class EqBand
    {
        public int Frequency { get; set; } = 1000;
        public float Gain { get; set; }
        public float Q { get; set; } = 1.2f;

        public EqBand() { }

        public EqBand(int frequency, float gain, float q = 1.2f)
        {
            Frequency = frequency;
            Gain = gain;
            Q = q;
        }

        public EqBand Clone() => new(Frequency, Gain, Q);
    }
}
