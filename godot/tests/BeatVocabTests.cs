#if GDUNIT_TESTS
using System;
using GameSim.Contracts;
using GdUnit4;
using GodotClient.Ui;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// P2-MEMORY-01: pins <see cref="BeatVocab"/> as the ONE short-label vocabulary for a <see
/// cref="BeatType"/>, and makes a new beat type without a label a red build.
///
/// <para><b>Reflective, deny-by-default.</b> This sweeps <see cref="Enum.GetValues{TEnum}"/>
/// rather than a hand-listed array of members — a hand-listed array stops covering the family
/// the moment someone adds a member (this repo has shipped that exact bug before); iterating the
/// enum itself means a new <see cref="BeatType"/> with no matching arm in <see
/// cref="BeatVocab.Label"/> throws at the switch's runtime default the instant this test asks
/// for its label, failing the suite rather than silently rendering blank or raw.</para>
/// </summary>
[TestSuite]
public class BeatVocabTests
{
    [TestCase]
    public void EveryBeatType_HasALabel_NeverTheRawEnumSpelling()
    {
        foreach (var beat in Enum.GetValues<BeatType>())
        {
            string label;
            try
            {
                label = BeatVocab.Label(beat);
            }
            catch (Exception ex)
            {
                throw new Exception($"{beat} has no BeatVocab label (deny-by-default tripped): {ex.Message}", ex);
            }

            AssertThat(label).OverrideFailureMessage($"{beat} has an empty BeatVocab label").IsNotEmpty();
            AssertThat(label)
                .OverrideFailureMessage($"{beat} still renders its own raw enum spelling")
                .IsNotEqual(beat.ToString());
        }
    }
}
#endif
