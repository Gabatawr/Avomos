using Avomos.Api.Models;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Avomos.Api.Services;

public class RiderSeeder
{
    private readonly EmbeddingService _embeddings;
    private readonly QdrantClient _qdrant;

    public RiderSeeder(EmbeddingService embeddings, QdrantClient qdrant)
    {
        _embeddings = embeddings;
        _qdrant = qdrant;
    }

    private static readonly string[] DefaultIds =
    [
        "a0000001-0000-4000-a000-000000000001",
        "a0000001-0000-4000-a000-000000000002",
        "a0000001-0000-4000-a000-000000000003",
        "a0000001-0000-4000-a000-000000000004",
        "a0000001-0000-4000-a000-000000000005",
        "a0000001-0000-4000-a000-000000000006"
    ];

    public static readonly RiderData[] Defaults =
    [
        new(DefaultIds[0], "default", "Cyber-Trap / Alt-Pop", 1,
            "v5.5", "mid-tempo (90-110 BPM), swung", "0.4 - 0.6", "0.7",
            "dark cyber-trap, mid-tempo, rubbery sub-bass, detuned bells, close-mic Russian male vocals",
            "dark cyber-trap, mid-tempo swung bounce, rubbery sub-bass, tight minimal trap drums, detuned bells, cold synth pads, close-mic Russian male vocals with whispered doubles",
            "upbeat, acoustic instruments, electric guitar, bright pop, live drums, female vocals, autotune",
            "[Intro: ambient pads, cold detuned bells, atmospheric]\n\n[Verse 1: Russian, close-mic male vocals, whispered doubles]\nWrite verse in Russian\nVoice should sound close, intimate\nMinimal instruments, only sub-bass and bells\nWords land right on the beat\n\n[Pre-Chorus: building energy, adding layers, filtered synth]\nTension rises, percussion layers are added\nTransition section before the explosion\n\n[Chorus: English, full production, wide layered harmonies]\nWrite the English hook here (bright and wide)\nCatchy melody that stays in your head\nExploding with distorted bass swells\nLet the sound open up fully\n\n[Verse 2: Russian, dynamic, tight trap beat]\nSecond verse in Russian\nEnergy slightly higher than the first\nRhythm gets tighter\n\n[Bridge: stripped back, slow expanding pulses, vulnerable vocals]\nContrast release, vulnerable vocals\nAlmost silence, only deep pads\nBefore the final chord\n\n[Chorus: English, full production, maximum energy]\nWrite the English hook here (repeated for climax)\nMaximum stereo widening and power\n\n[Outro: fade out, distant siren synths, ambient reprise]\nSmooth fade into darkness"),

        new(DefaultIds[1], "default", "Glitchy Industrial / Electro", 2,
            "v5.5", "driving (120-130 BPM), relentless 4/4", "0.6 - 0.7", "0.8",
            "cold industrial electro, metallic percussion, distorted synth bass, spoken male vocals",
            "cold industrial electro, driving relentless 4/4 beat, metallic percussion, distorted synth bass, crisp synth stabs, glitched vocal chops, spoken male vocals with robotic harmonies",
            "acoustic guitar, warm piano, soft acoustic drums, emotional ballad, female vocals, ambient drone",
            "[Intro: filtered drums, metallic clanks, glitchy static]\n\n[Verse 1: spoken flow, robotic cadence]\nWords are delivered monotonously, like commands\nBass cuts space into pieces\nIndustrial cold, precise rhythm\n\n[Chorus: robotic harmonies, distorted synth stabs, relentless beat]\nVoices multiply in metallic echoes\nSynths explode in the foreground\nWall of sound\n\n[Verse 2: spoken flow, tight glitched percussion]\nContinuing the recitative\nRhythm glitches, glitches intensify\nElectronic madness\n\n[Bridge: drum machine solo, high-pass filter, digital noise]\n\n[Chorus: robotic harmonies, maximum volume]\nRepeat chorus with maximum sound density\n\n[Outro: sudden stop, digital static decay]\nAbrupt break or decay into digital noise"),

        new(DefaultIds[2], "default", "Ethereal Ambient / Drone", 3,
            "v4 or v5.5", "glacial (very slow)", "0.7 - 0.9", "0.6",
            "ambient drone, evolving pads, low analog rumble, ethereal female vocals",
            "ambient drone, glacial tempo, slowly drifting melodic loops, evolving synth pads, deep sub-bass pulse, granular textures, distant ethereal female vocals with heavy reverb",
            "drums, percussion, beats, fast tempo, heavy guitars, aggressive vocals, pop hooks",
            "[Intro: low drone, expanding silence, field recordings]\n\n[Verse: whispered vocals, slow expanding pulses]\nWords float through endless reverb\nThere is no clear rhythm here\nSound breathes, rolls in waves\nDrifting tonalities\n\n[Chorus: ethereal wordless vocals, shimmering harmonic drift]\n(Ooh... Aah... Ethereal vocals without words)\nShimmering sound textures\nMaximum stereo width\n\n[Bridge: near-silence, fragile held tones, organic static]\n\n[Outro: slow fade to absolute silence, tape hiss]\nSlow fading into white noise"),

        new(DefaultIds[3], "default", "Moody Alt-Rock / Pop-Rock", 4,
            "v5.5", "mid-tempo (100-110 BPM)", "0.3", "0.8",
            "moody alt-rock, muted electric guitars, dry male vocals, wide chorus",
            "moody mid-tempo alt-rock, palm-muted electric guitars in verses, dry upfront male vocals, wide overdriven guitar chords on chorus, tight acoustic drums, melancholic atmosphere",
            "synthwave, 808 bass, trap hi-hats, electronic drops, brass, bright synth leads, auto-tune",
            "[Intro: clean acoustic guitar riff, simple drum click]\n\n[Verse 1: close-mic male vocals, palm-muted guitars]\nDry, honest vocals without excessive effects\nGuitars sound muted\nThe story begins quietly\n\n[Pre-Chorus: building tension, adding bass, drum roll]\nBass guitar kicks in, drum density builds\nThe wall of sound prepares to open up\n\n[Chorus: wide overdriven guitars, soaring vocals, full band]\nGuitar explosion, overdriven chords\nVocals become powerful and soaring\nEmotional peak\n\n[Verse 2: close-mic, dynamic bassline]\nDrop back into verses\nDrums hold the groove, guitar enters with sparse strokes\n\n[Bridge: stripped back, piano and raw vocals only]\nFull release, only keys and voice\nAll the song's vulnerability concentrated here\n\n[Chorus: wide overdriven guitars, maximum energy]\nFinal powerful chorus\n\n[Outro: guitar feedback, slow drum fadeout]\nGuitar amplifier feedback, slow drum fade"),

        new(DefaultIds[4], "default", "Cinematic / Epic / Shamanic", 5,
            "v4.5 or v5.5", "building (slow to fast)", "0.5 - 0.7", "0.8",
            "cinematic orchestral, shamanic percussion, building tension, heroic choir",
            "cinematic orchestral, slow building tension, shamanic acoustic drums, deep brass swell, sweeping string section, heroic male vocal choir harmonies, epic soundscape",
            "synthesizers, electronic beats, trap drums, auto-tune, rap, pop structure, electric guitar",
            "[Intro: distant hand drums, organic woodwind, low strings]\n\n[Verse: deep baritone vocals, shamanic chant cadence]\nDeep throat vocals\nRepeating hypnotic phrases\nPercussion holds an ancient, primal rhythm\n\n[Pre-Chorus: building orchestration, brass swells, faster tempo]\nOrchestra expands, brass enters\nTempo accelerates and thickens\n\n[Chorus: heroic choir, sweeping strings, thunderous timpani rolls]\nChoir explodes with a triumphant melody\nPowerful timpani strikes\nEpic grandeur\n\n[Bridge: solo acoustic instrument, quiet reflection, strings whisper]\nA lone flute or string instrument in silence\nShort breather before the finale\n\n[Chorus: heroic choir, maximum orchestration, epic climax]\nFinal battle, all instruments at maximum energy\n\n[Outro: decaying reverb, final drum strike, silent wind]\nReverb tail, a lone percussion hit and silence"),

        new(DefaultIds[5], "default", "Heavy Metal", 6,
            "v5.5", "fast (130-160 BPM), driving", "0.2 - 0.4", "0.8",
            "heavy metal, distorted palm-muted guitars, double bass drums, aggressive male vocals",
            "heavy metal, fast tempo, palm-muted distorted guitar riffs, driving double bass drums, aggressive shouted male vocals, dark atmosphere, raw production, tight rhythm section",
            "synthesizers, electronic beats, clean vocals, pop melody, trap drums, auto-tune, acoustic instruments, lo-fi",
            "[Intro: heavy distorted guitar riff, drums build]\n\n[Verse 1: shouted vocals, palm-muted chugging, driving double bass]\nWords spit like venom\nGuitars chug in tight sync with the kick drum\nRhythm section locked in\nDarkness builds with every bar\n\n[Pre-Chorus: rising tension, snare rolls, open chords]\nThe riff opens up\nDrums swell toward the breakdown\n\n[Chorus: screamed vocals, full distortion, maximum aggression]\nWall of distortion crashes down\nDouble bass relentless\nCatchy but crushing hook\nEverything at full volume\n\n[Bridge: half-time groove, solo guitar, stripped drums]\nGuitar solo over pounding rhythm\nA moment of pure instrumental fury\n\n[Chorus: screamed vocals, full intensity]\nFinal assault, all energy released\n\n[Outro: heavy chugging fade, final crash cymbal]\nRiffs slow down to a crushing halt\nOne last hit")
    ];

    public async Task SeedIfEmptyAsync(CancellationToken ct = default)
    {
        var collections = await _qdrant.ListCollectionsAsync(ct);
        if (collections.Contains(RiderDocument.Collection))
        {
            var info = await _qdrant.GetCollectionInfoAsync(RiderDocument.Collection, ct);
            if (info.PointsCount > 0) return;
            await _qdrant.DeleteCollectionAsync(RiderDocument.Collection, cancellationToken: ct);
        }

        await _qdrant.CreateCollectionAsync(RiderDocument.Collection, RiderDocument.VectorConfig, cancellationToken: ct);

        foreach (var rider in Defaults)
        {
            var vec = await _embeddings.EmbedCachedAsync(rider.ShortStyle, "rider", ct);
            var pt = RiderDocument.ToPoint(rider, vec);
            await _qdrant.UpsertAsync(RiderDocument.Collection, [pt], cancellationToken: ct);
        }
    }
}
