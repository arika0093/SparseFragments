extern alias Playground;

using Playground::SparseTypes.Playground.Models;
using Playground::SparseTypes.Playground.Pages.Case3;

namespace SparseTypes.Tests.Display;

// Guards the #292 display pipeline: direct indented source-generated output
// must stay byte-identical to the old serialize/parse/rewrite pipeline.
public sealed class PlaygroundJsonDisplayTests
{
    private static PlaygroundSettings EscapedModel() =>
        new()
        {
            Enabled = false,
            RetryCount = 7,
            Label = "a\"b\\c\nü→ñ",
            Password = "s3cret!",
            Nested = new PlaygroundNested { Host = "exa\"mple\nhost", Port = 1 },
            Plugins = ["a,b", "c\"d"],
            Tags = new HashSet<string>(["x", "y"], StringComparer.Ordinal),
            Tasks =
            [
                new PlaygroundTask
                {
                    Id = "t1",
                    Title = "Tàsk \"1\"",
                    Points = 3,
                },
                new PlaygroundTask
                {
                    Id = "t2",
                    Title = "plain",
                    Points = 0,
                },
            ],
        };

    private static EnterpriseCatalogItem CatalogSample() =>
        new()
        {
            Overview = new CatalogOverview
            {
                ItemCode = "EQ-2048",
                Name = "Modular \"Drive\" Unit\nü",
                Category = "Industrial",
                Owner = "M. Tanaka",
                Active = true,
                AlternateCodes = ["MDU-2048"],
            },
            Commercial = new CatalogCommercialDetails
            {
                Customer = "Northwind",
                Region = "APAC",
                ContractCode = "NW-1",
                Currency = "USD",
                UnitPrice = 2480.00m,
                AnnualVolume = 1200,
            },
            Operations = new CatalogOperationsDetails
            {
                Facility = "Nagoya",
                Warehouse = "WH-A3",
                LeadTimeDays = 18,
                QualityStandard = "ISO 9001",
                InspectionRequired = true,
                PackagingInstruction = "Moisture barrier",
            },
            Workflow = new CatalogWorkflow
            {
                Steps =
                [
                    new CatalogWorkStep
                    {
                        Id = "cut",
                        Name = "Cutting",
                        ResponsibleTeam = "Machining",
                        StandardHours = 1.5m,
                    },
                ],
            },
            WorkMasters =
            [
                new CatalogWorkMaster
                {
                    StepId = "step-0",
                    WorkCenter = "WC-0",
                    Equipment = "Press \"A\"",
                    SetupMinutes = 12.5m,
                    Instruction = "Step 0 instruction ü",
                    SupervisorApproval = true,
                },
            ],
        };

    private static PlaygroundSettings.ChangeSet SettingsChanges()
    {
        var baseline = FragmentEditState.LowerDefaults().Build();
        var patch = new PatchEditState
        {
            EnabledOp = PatchKind.Keep,
            RetryOp = PatchKind.Set,
            RetryValue = 5,
            LabelOp = NullPatchKind.SetNull,
            NestedOp = NestedPatchKind.Set,
            HostOp = PatchKind.Remove,
            PortOp = PatchKind.Set,
            PortValue = 9000,
            PluginsOp = PatchKind.Remove,
        }.Build();
        return PlaygroundSettings.ChangeSet.FromPatch(
            Optional<PlaygroundSettings.Fragment?>.Present(baseline),
            patch
        );
    }

    // STJ indents with Environment.NewLine while raw literals carry the
    // source file's endings; normalize both sides before comparing.
    private static string Normalize(string value) => value.ReplaceLineEndings("\n");

    [Test]
    public void ModelJsonKeepsOrderIndentationAndEscaping()
    {
        PlaygroundJson
            .WriteModel(EscapedModel())
            .ReplaceLineEndings("\n")
            .ShouldBe(
                Normalize(
                    """
                    {
                      "Enabled": false,
                      "RetryCount": 7,
                      "Label": "a\u0022b\\c\n\u00FC\u2192\u00F1",
                      "Password": "s3cret!",
                      "Nested": {
                        "Host": "exa\u0022mple\nhost",
                        "Port": 1
                      },
                      "Plugins": [
                        "a,b",
                        "c\u0022d"
                      ],
                      "Tags": [
                        "x",
                        "y"
                      ],
                      "Tasks": [
                        {
                          "Id": "t1",
                          "Title": "T\u00E0sk \u00221\u0022",
                          "Points": 3
                        },
                        {
                          "Id": "t2",
                          "Title": "plain",
                          "Points": 0
                        }
                      ]
                    }
                    """
                )
            );
    }

    [Test]
    public void NullishModelKeepsAbsentVersusNull()
    {
        PlaygroundJson
            .WriteModel(
                new PlaygroundSettings
                {
                    Label = null,
                    Password = null,
                    Nested = null,
                }
            )
            .ReplaceLineEndings("\n")
            .ShouldBe(
                Normalize(
                    """
                    {
                      "Enabled": true,
                      "RetryCount": 3,
                      "Label": null,
                      "Nested": null,
                      "Plugins": [],
                      "Tags": [],
                      "Tasks": []
                    }
                    """
                )
            );
    }

    [Test]
    public void MaskedSummaryHidesPasswordWithoutMutatingTheModel()
    {
        var model = EscapedModel();
        PlaygroundJson
            .WriteMaskedSettingsSummary(model)
            .ShouldContain("\"Password\": \"[masked]\"");
        model.Password.ShouldBe("s3cret!");
    }

    [Test]
    public void FragmentJsonKeepsCanonicalOrderAndAbsentVersusNull()
    {
        var lower = FragmentEditState.LowerDefaults().Build();
        var higher = FragmentEditState.HigherDefaults().Build();

        PlaygroundJson
            .WriteFragment(lower.Merge(higher))
            .ReplaceLineEndings("\n")
            .ShouldBe(
                Normalize(
                    """
                    {
                      "Enabled": true,
                      "RetryCount": 3,
                      "Label": null,
                      "Nested": {
                        "Host": "localhost",
                        "Port": 8080
                      },
                      "Plugins": [
                        "base-plugin",
                        "extra-plugin"
                      ]
                    }
                    """
                )
            );

        // Sparse layers omit absent members but keep explicit nulls in order.
        PlaygroundJson
            .WriteFragment(higher)
            .ReplaceLineEndings("\n")
            .ShouldBe(
                Normalize(
                    """
                    {
                      "Label": null,
                      "Nested": {
                        "Port": 8080
                      },
                      "Plugins": [
                        "extra-plugin"
                      ]
                    }
                    """
                )
            );
    }

    [Test]
    public void RosterModelAndFragmentJsonMatch()
    {
        var golden = """
            {
              "Quests": [
                {
                  "Id": "a",
                  "Title": "First",
                  "Points": 10,
                  "Scores": [
                    10,
                    20
                  ]
                },
                {
                  "Id": "b",
                  "Title": "Second",
                  "Points": 20,
                  "Scores": [
                    30
                  ]
                },
                {
                  "Id": "c",
                  "Title": "Third",
                  "Points": 30,
                  "Scores": [
                    40,
                    50
                  ]
                }
              ]
            }
            """;
        var before = RosterDefaults.Before();
        Normalize(PlaygroundJson.WriteRosterModel(before)).ShouldBe(Normalize(golden));
        Normalize(PlaygroundJson.WriteRosterFragment(PlaygroundRoster.Fragment.From(before)))
            .ReplaceLineEndings("\n")
            .ShouldBe(Normalize(golden));
    }

    [Test]
    public void CatalogJsonKeepsDecimalsAndEscaping()
    {
        PlaygroundJson
            .WriteEnterpriseCatalog(CatalogSample())
            .ReplaceLineEndings("\n")
            .ShouldBe(
                Normalize(
                    """
                    {
                      "Overview": {
                        "ItemCode": "EQ-2048",
                        "Name": "Modular \u0022Drive\u0022 Unit\n\u00FC",
                        "Category": "Industrial",
                        "Owner": "M. Tanaka",
                        "Active": true,
                        "AlternateCodes": [
                          "MDU-2048"
                        ]
                      },
                      "Commercial": {
                        "Customer": "Northwind",
                        "Region": "APAC",
                        "ContractCode": "NW-1",
                        "Currency": "USD",
                        "UnitPrice": 2480.00,
                        "AnnualVolume": 1200,
                        "PriceTiers": []
                      },
                      "Operations": {
                        "Facility": "Nagoya",
                        "Warehouse": "WH-A3",
                        "LeadTimeDays": 18,
                        "QualityStandard": "ISO 9001",
                        "InspectionRequired": true,
                        "PackagingInstruction": "Moisture barrier",
                        "Documents": []
                      },
                      "Workflow": {
                        "Steps": [
                          {
                            "Id": "cut",
                            "Name": "Cutting",
                            "ResponsibleTeam": "Machining",
                            "StandardHours": 1.5
                          }
                        ]
                      },
                      "WorkMasters": [
                        {
                          "StepId": "step-0",
                          "WorkCenter": "WC-0",
                          "Equipment": "Press \u0022A\u0022",
                          "SetupMinutes": 12.5,
                          "Instruction": "Step 0 instruction \u00FC",
                          "SupervisorApproval": true
                        }
                      ]
                    }
                    """
                )
            );
    }

    [Test]
    public void ChangePayloadRoundTripSurvivesPayloadSerialization()
    {
        var changes = SettingsChanges();
        var imported = PlaygroundJson.ReadSettingsChangeSet(
            PlaygroundJson.WriteSettingsChangeSet(changes)
        );
        imported.IsEmpty.ShouldBe(changes.IsEmpty);

        var baseline = FragmentEditState.LowerDefaults().Build();
        PlaygroundJson
            .WriteFragment(baseline.Apply(imported.ToPatch()))
            .ShouldBe(PlaygroundJson.WriteFragment(baseline.Apply(changes.ToPatch())));
    }

    [Test]
    public void RepeatedDisplaySerializationIsStableAcrossThreads()
    {
        var model = EscapedModel();
        var merged = FragmentEditState
            .LowerDefaults()
            .Build()
            .Merge(FragmentEditState.HigherDefaults().Build());
        var expectedModel = PlaygroundJson.WriteModel(model);
        var expectedFragment = PlaygroundJson.WriteFragment(merged);
        var expectedChanges = PlaygroundJson.WriteSettingsChangeSet(SettingsChanges());

        Parallel.For(
            0,
            64,
            _ =>
            {
                PlaygroundJson.WriteModel(model).ShouldBe(expectedModel);
                PlaygroundJson.WriteFragment(merged).ShouldBe(expectedFragment);
                PlaygroundJson.WriteSettingsChangeSet(SettingsChanges()).ShouldBe(expectedChanges);
            }
        );
    }

    [Test]
    public void OptionFactoriesReturnIndependentInstances()
    {
        var first = PlaygroundJson.ChangeSetOptions();
        var second = PlaygroundJson.ChangeSetOptions();
        first.ShouldNotBeSameAs(second);
        first.WriteIndented = false;
        second.WriteIndented.ShouldBeTrue();

        var fragments = PlaygroundJson.FragmentOptions();
        fragments.ShouldNotBeSameAs(PlaygroundJson.FragmentOptions());

        // Cached display paths ignore factory mutations.
        PlaygroundJson
            .WriteSettingsChangeSet(SettingsChanges())
            .ShouldContain("\"version\": \"0.1\"");
    }

    [Test]
    public void PrettyStillPassesThroughExternalText()
    {
        PlaygroundJson.Pretty("{ \"a\": 1 }").ShouldContain("\"a\": 1");
        PlaygroundJson.Pretty("not json").ShouldBe("not json");
    }
}
