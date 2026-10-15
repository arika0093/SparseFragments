using System.ComponentModel;
using System.Reflection;

namespace SparseTypes.PublicApi.Tests;

/// <summary>Guards the SparseTypes API boundary from, reduced by.</summary>
public sealed class SparseTypesApiAudienceTests
{
    private static readonly string[] FirstClass =
    [
        "SparseTypes.Optional`1",
        "SparseTypes.FragmentOperation`1",
        "SparseTypes.FragmentOperationKind",
        "SparseTypes.ISparseEditSession`1",
        "SparseTypes.ISparseEditSession`2",
        "SparseTypes.ISparseEditSessionWithPaths`2",
        "SparseTypes.ISparseEditSessionModelAccessor`1",
        "SparseTypes.EditSessionGroup",
        "SparseTypes.EditSessionGroupChangedPath",
        "SparseTypes.EditSessionGroupRegistration`2",
        "SparseTypes.EditSessionGroupRemovalPolicy",
        "SparseTypes.EditSessionGroupSubmission",
        "SparseTypes.EditSessionGroupValidationException",
        "SparseTypes.EditSessionGroupValidationIssue",
        "SparseTypes.EditSessionSectionHandle`2",
        "SparseTypes.EditSessionStaleSubmissionException",
        "SparseTypes.EditSessionSubmissionEntry`1",
        "SparseTypes.EditSessionSubmittedSection",
        "SparseTypes.ISparseEditHistory",
        "SparseTypes.EditHistoryOptions",
        "SparseTypes.SparseEditHistory`1",
        "SparseTypes.MergeMode",
        "SparseTypes.SparseModelAttribute",
        "SparseTypes.SparseIgnoreAttribute",
        "SparseTypes.SparseRedactBeforeAttribute",
        "SparseTypes.SparseKeyAttribute",
        "SparseTypes.SparseTemporaryKeyAttribute",
        "SparseTypes.SparseMergeAttribute",
        "SparseTypes.SparseCompareAttribute",
        "SparseTypes.SparseRebasePolicyAttribute",
        "SparseTypes.SparseCloneReferenceSafeAttribute",
    ];

    private static readonly string[] Plumbing =
    [
        "SparseTypes.ChangePayloadWire",
        "SparseTypes.CompilerServices.SparseCloneContextScope",
        "SparseTypes.CompilerServices.SparseKeyValidationScope`1",
        "SparseTypes.CompilerServices.SparseFragmentRuntime",
    ];

    [Test]
    public void EveryPublicTypeIsClassified()
    {
        var exported = typeof(SparseTypes.SparseModelAttribute)
            .Assembly.GetExportedTypes()
            .Select(static type => type.FullName ?? type.Name)
            .OrderBy(static name => name)
            .ToArray();
        exported.ShouldNotBeEmpty();

        var offenders = exported
            .Where(static name =>
                !FirstClass.Contains(name) && !IsAdvanced(name) && !Plumbing.Contains(name)
            )
            .ToArray();
        offenders.ShouldBeEmpty();
    }

    [Test]
    public void FirstClassTypesStayVisible()
    {
        var offenders = FirstClass
            .Select(Resolve)
            .Where(static type => HasBrowsableHiding(type))
            .Select(static type => type.FullName ?? type.Name)
            .ToArray();
        offenders.ShouldBeEmpty();
    }

    [Test]
    public void ExtensionAndToolingTypesStayAdvanced()
    {
        var offenders = typeof(SparseTypes.SparseModelAttribute)
            .Assembly.GetExportedTypes()
            .Where(static type =>
                !FirstClass.Contains(type.FullName) && !Plumbing.Contains(type.FullName)
            )
            .Where(static type => !HasAdvancedHiding(type))
            .Select(static type => type.FullName ?? type.Name)
            .OrderBy(static name => name)
            .ToArray();
        offenders.ShouldBeEmpty();
    }

    [Test]
    public void PlumbingTypesStayHidden()
    {
        var offenders = Plumbing
            .Select(Resolve)
            .Where(static type => !HasNeverHiding(type))
            .Select(static type => type.FullName ?? type.Name)
            .ToArray();
        offenders.ShouldBeEmpty();
    }

    [Test]
    public void GenericSessionImplementationIsNotExportedByTheRuntime()
    {
        var assembly = typeof(SparseTypes.SparseModelAttribute).Assembly;
        assembly.GetType("SparseTypes.SparseEditSession`5").ShouldBeNull();
        assembly.GetType("SparseTypes.SparseEditSession`6").ShouldBeNull();
        assembly.GetType("SparseTypes.SparseEditSessionConfiguration`6").ShouldBeNull();
    }

    [Test]
    public void SessionCapabilityInterfacesStayAdvanced()
    {
        // Capability interfaces implemented by generated sessions stay out of
        // first-class IntelliSense. Pin each generic definition explicitly so a
        // new capability surface without classification fails here, not in CI.
        var capabilityTypes = new[]
        {
            "SparseTypes.ISparseEditSessionWithChildren`2",
            "SparseTypes.ISparseEditSessionWithChangeSet`2",
            "SparseTypes.ISparseEditSessionWithChildStatus`2",
            "SparseTypes.SparseChildSessionStatus",
        };
        var offenders = capabilityTypes.Where(static name => !IsAdvanced(name)).ToArray();
        offenders.ShouldBeEmpty();
    }

    private static bool IsAdvanced(string fullName) => HasAdvancedHiding(Resolve(fullName));

    private static Type Resolve(string fullName) =>
        typeof(SparseTypes.SparseModelAttribute).Assembly.GetType(fullName)
        ?? throw new InvalidOperationException($"Unknown SparseTypes type '{fullName}'.");

    private static bool HasBrowsableHiding(MemberInfo member) =>
        member.GetCustomAttribute<EditorBrowsableAttribute>()
            is { State: not EditorBrowsableState.Always };

    private static bool HasAdvancedHiding(MemberInfo member) =>
        member.GetCustomAttribute<EditorBrowsableAttribute>()
            is { State: EditorBrowsableState.Advanced };

    private static bool HasNeverHiding(MemberInfo member) =>
        member.GetCustomAttribute<EditorBrowsableAttribute>()
            is { State: EditorBrowsableState.Never };
}
