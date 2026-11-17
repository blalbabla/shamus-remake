using Shamus.Game.Audio;
using Xunit;

public class AudioTests
{
    [Fact]
    public void AmbientSynthIsFiniteAudibleAndBounded()
    {
        var synth = new AmbientSynth(42);
        var buf = new short[4096 * 2];
        int peak = 0; double energy = 0; int chunks = 0;
        for (int c = 0; c < 200; c++)               // ~18 s
        {
            synth.TensionTarget = c > 100 ? 1f : 0f;
            synth.Render(buf, 4096);
            foreach (var s in buf) { peak = Math.Max(peak, Math.Abs((int)s)); energy += (double)s * s; }
            chunks++;
        }
        Assert.InRange(peak, 500, 32767);
        Assert.True(Math.Sqrt(energy / (chunks * buf.Length)) > 50, "ambience is too quiet");
    }

    [Fact]
    public void AmbientSynthDoesNotRepeat()
    {
        var a = new AmbientSynth(1); var b = new AmbientSynth(2);
        var x = new short[2048]; var y = new short[2048];
        a.Render(x, 1024); b.Render(y, 1024);
        Assert.NotEqual(x, y);
    }

    [Theory]
    [InlineData(Sfx.Shot)] [InlineData(Sfx.PlayerDie)] [InlineData(Sfx.GameOver)] [InlineData(Sfx.ShadowDie)] [InlineData(Sfx.NewLoop)] [InlineData(Sfx.Keyhole)]
    public void SfxGeneratorsProduceFiniteSoundWithinRange(Sfx sfx)
    {
        for (int v = 0; v < ProceduralSfx.VariantCount(sfx); v++)
        {
            var s = ProceduralSfx.Create(sfx, v);
            Assert.NotEmpty(s);
            Assert.All(s, f => Assert.True(float.IsFinite(f) && Math.Abs(f) <= 1.0f));
            Assert.Contains(s, f => Math.Abs(f) > 0.1f);
        }
    }
}
