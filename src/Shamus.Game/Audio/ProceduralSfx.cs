namespace Shamus.Game.Audio;

public enum Sfx { Shot, HitWall, HitBarrier, BulletsClash, EnemyShot, EnemyDie, PlayerDie, GameOver, Key, Door, Potion, Bonus, PodOpen, ShadowWarn, ShadowAppear, ShadowDie, RoomEnter, NewLoop, Keyhole, EnemyImpact }

/// <summary>All sounds are synthesised: one deterministic generator per effect, seeded by the variant index.</summary>
public static class ProceduralSfx
{
    public static int VariantCount(Sfx s) => s switch
    {
        Sfx.GameOver or Sfx.NewLoop or Sfx.ShadowDie or Sfx.ShadowWarn or Sfx.ShadowAppear => 2,
        _ => 5,
    };

    public static float[] Create(Sfx sfx, int variant)
    {
        var rng = new Random(((int)sfx + 1) * 1009 + variant * 31);
        float j(float amt) => 1f + ((float)rng.NextDouble() * 2 - 1) * amt; // +/- jitter
        float[] b;
        switch (sfx)
        {
            case Sfx.Shot:
                b = Dsp.Buf(1.15f);
                // launch: muzzle crack, deep boom, metallic breech
                Dsp.AddNoise(b, rng, 0f, 0.12f, 16000, 4000, 1.0f, 55);
                Dsp.AddSweep(b, 0f, 0.55f, 130 * j(0.05f), 34, 0.9f, 6, 2);
                Dsp.AddSweep(b, 0f, 0.35f, 75, 38, 0.6f, 8);
                Dsp.AddSweep(b, 0.005f, 0.10f, 3200 * j(0.1f), 700, 0.35f, 26, 1);
                // the shell's burning fuel: a bright hiss with a low roar and a few crackles
                Dsp.AddNoise(b, rng, 0.10f, 1.0f, 7000, 2500, 0.42f, 0.7f);
                Dsp.AddNoise(b, rng, 0.10f, 0.85f, 1200, 300, 0.28f, 1.5f);
                for (int k = 0; k < 6; k++)
                {
                    float t0 = 0.15f + k * 0.13f + (float)rng.NextDouble() * 0.05f;
                    Dsp.AddNoise(b, rng, t0, 0.025f, 9000, 5000, 0.35f, 80);
                }
                break;
            case Sfx.HitWall:
                b = Dsp.Buf(0.95f);
                // impact: sharp crack, explosion body, debris shards
                Dsp.AddNoise(b, rng, 0f, 0.08f, 16000, 5000, 1.0f, 60);
                Dsp.AddSweep(b, 0f, 0.65f, 120 * j(0.08f), 32, 1.0f, 5, 2);
                Dsp.AddSweep(b, 0f, 0.5f, 70, 30, 0.7f, 6);
                Dsp.AddNoise(b, rng, 0.02f, 0.8f, 5500, 800, 0.65f, 4.5f);
                Dsp.AddNoise(b, rng, 0.05f, 0.7f, 1200, 250, 0.35f, 3);
                for (int k = 0; k < 4; k++)
                    Dsp.AddSweep(b, 0.03f + k * 0.04f, 0.08f, 2400 * j(0.2f), 900, 0.12f, 25, 1);
                break;
            case Sfx.HitBarrier:
                b = Dsp.Buf(0.35f);
                Dsp.AddBell(b, 0, 760 * j(0.12f), 0.35f, 9);
                break;
            case Sfx.BulletsClash:
                b = Dsp.Buf(0.14f);
                Dsp.AddNoise(b, rng, 0, 0.12f, 6000, 1500, 0.8f, 28);
                Dsp.AddSweep(b, 0, 0.1f, 2400 * j(0.15f), 900, 0.3f, 25, 1);
                break;
            case Sfx.EnemyShot:
                b = Dsp.Buf(1.6f);
                // launcher: servo click and magnetic thunk
                Dsp.AddNoise(b, rng, 0f, 0.04f, 12000, 5000, 0.6f, 70);
                Dsp.AddSweep(b, 0f, 0.10f, 220 * j(0.1f), 70, 0.5f, 25, 1);
                // energy discharge
                Dsp.AddSweep(b, 0.01f, 0.16f, 380 * j(0.1f), 1600 * j(0.1f), 0.45f, 10, 1);
                Dsp.AddNoise(b, rng, 0.01f, 0.12f, 9000, 3000, 0.35f, 18);
                // plasma bolt in flight: electric whine with vibrato, ion hiss and crackle
                Dsp.AddSweep(b, 0.08f, 1.45f, 1350 * j(0.04f), 700, 0.16f, 1.1f, 0, 18);
                Dsp.AddSweep(b, 0.08f, 1.35f, 675 * j(0.04f), 350, 0.08f, 1.2f, 1);
                Dsp.AddNoise(b, rng, 0.08f, 1.2f, 6000, 1800, 0.16f, 1.6f);
                for (int k = 0; k < 4; k++)
                    Dsp.AddNoise(b, rng, 0.2f + k * 0.25f, 0.02f, 9000, 6000, 0.22f, 80);
                break;
            case Sfx.EnemyImpact:
                b = Dsp.Buf(0.45f);
                // plasma splash: zap, short ring, thud and sparks
                Dsp.AddNoise(b, rng, 0f, 0.05f, 12000, 4000, 0.8f, 55);
                Dsp.AddSweep(b, 0f, 0.18f, 1600 * j(0.15f), 260, 0.4f, 20, 1);
                Dsp.AddBell(b, 0.01f, 1200 * j(0.1f), 0.25f, 18);
                Dsp.AddSweep(b, 0f, 0.3f, 90 * j(0.1f), 45, 0.3f, 15);
                Dsp.AddNoise(b, rng, 0.02f, 0.35f, 5000, 1200, 0.25f, 8);
                break;
            case Sfx.EnemyDie:
                b = Dsp.Buf(0.8f);
                // robot hit: crack, body explosion, debris and electrical sparks
                Dsp.AddNoise(b, rng, 0f, 0.08f, 15000, 5000, 0.9f, 55);
                Dsp.AddSweep(b, 0f, 0.5f, 180 * j(0.1f), 40, 0.8f, 6, 2);
                Dsp.AddNoise(b, rng, 0.02f, 0.7f, 6000, 500, 0.6f, 5);
                Dsp.AddSweep(b, 0f, 0.6f, 650 * j(0.1f), 90, 0.25f, 4, 1, 20);
                for (int k = 0; k < 5; k++)
                    Dsp.AddSweep(b, 0.05f + k * 0.05f, 0.04f, 3000 * j(0.2f), 1200, 0.12f, 30);
                break;
            case Sfx.PlayerDie:
                b = Dsp.Buf(1.0f);
                Dsp.AddSweep(b, 0, 0.9f, 900 * j(0.05f), 55, 0.45f, 2.5f, 2, 22);
                Dsp.AddNoise(b, rng, 0, 0.9f, 4500, 300, 0.7f, 3);
                break;
            case Sfx.GameOver:
                b = Dsp.Buf(2.6f);
                float[] seq = { 392, 330, 262, 196 };
                for (int i = 0; i < 4; i++) Dsp.AddSweep(b, i * 0.55f, 0.9f, seq[i] * (variant == 1 ? 0.94f : 1f), seq[i] * 0.97f, 0.3f, 2.2f, 2);
                Dsp.AddSweep(b, 2.2f, 0.4f, 98, 80, 0.35f, 3);
                break;
            case Sfx.Key:
                b = Dsp.Buf(0.7f);
                Dsp.AddBell(b, 0, 1320 * j(0.03f), 0.3f, 7);
                Dsp.AddBell(b, 0.1f, 1760 * j(0.03f), 0.3f, 7);
                break;
            case Sfx.Door:
                b = Dsp.Buf(0.7f);
                Dsp.AddNoise(b, rng, 0, 0.4f, 300, 3500, 0.35f, 5);
                Dsp.AddSweep(b, 0.3f, 0.3f, 160 * j(0.1f), 70, 0.8f, 12);
                Dsp.AddNoise(b, rng, 0.3f, 0.1f, 1800, 300, 0.7f, 30);
                break;
            case Sfx.Potion:
                b = Dsp.Buf(0.7f);
                float[] arp = { 523, 659, 784, 1047 };
                for (int i = 0; i < 4; i++) Dsp.AddSweep(b, i * 0.07f, 0.35f, arp[i] * j(0.01f), arp[i] * j(0.01f), 0.3f, 7);
                break;
            case Sfx.Bonus:
                b = Dsp.Buf(0.6f);
                for (int i = 0; i < 6; i++) Dsp.AddBell(b, i * 0.05f, 1500 + rng.Next(1500), 0.15f, 10);
                break;
            case Sfx.PodOpen:
                b = Dsp.Buf(1.0f);
                Dsp.AddSweep(b, 0, 0.9f, 180, 1400 * j(0.05f), 0.4f, 1.5f, 1);
                Dsp.AddNoise(b, rng, 0.5f, 0.45f, 500, 5000, 0.3f, 4);
                break;
            case Sfx.ShadowWarn:
                b = Dsp.Buf(1.3f);
                for (int i = 0; i < 4; i++) Dsp.AddSweep(b, i * 0.3f, 0.28f, 72 * (variant == 1 ? 1.2f : 1f), 64, 0.8f, 5);
                break;
            case Sfx.ShadowAppear:
                b = Dsp.Buf(1.4f);
                Dsp.AddSweep(b, 0, 1.3f, 110 * (variant + 1), 40, 0.6f, 2.2f, 2, 8);
                Dsp.AddNoise(b, rng, 0, 1.0f, 800, 120, 0.5f, 2.5f);
                break;
            case Sfx.ShadowDie:
                b = Dsp.Buf(2.0f);
                Dsp.AddSweep(b, 0, 1.6f, 140, 1100, 0.3f, 2, 2, 12);
                Dsp.AddNoise(b, rng, 0, 1.8f, 6000, 200, 0.7f, 2);
                Dsp.AddBell(b, 1.0f, 880 * (variant == 1 ? 1.12f : 1f), 0.3f, 3);
                break;
            case Sfx.RoomEnter:
                b = Dsp.Buf(0.12f);
                Dsp.AddSweep(b, 0, 0.1f, 420 * j(0.1f), 520 * j(0.1f), 0.18f, 22);
                break;
            case Sfx.Keyhole:
                b = Dsp.Buf(1.1f);
                Dsp.AddSweep(b, 0f, 0.9f, 95 * j(0.05f), 48, 0.7f, 2.2f, 2, 9);   // the energy wall gives way
                Dsp.AddNoise(b, rng, 0f, 0.7f, 250, 2600, 0.4f, 3);                 // crackle of the field
                Dsp.AddBell(b, 0.12f, 1320 * j(0.04f), 0.22f, 8);                   // key turns
                Dsp.AddBell(b, 0.30f, 990 * j(0.04f), 0.18f, 10);
                break;
            default: // NewLoop fanfare
                b = Dsp.Buf(1.8f);
                float[] fan = { 392, 523, 659, 784, 1047 };
                for (int i = 0; i < fan.Length; i++) Dsp.AddSweep(b, i * 0.14f, 0.7f, fan[i], fan[i], 0.28f, 3, variant == 1 ? 1 : 0);
                break;
        }
        Dsp.FadeEdges(b);
        Dsp.Normalize(b, 0.85f);
        return b;
    }
}
