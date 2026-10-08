using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Text;
using PublicApiGenerator;

namespace SparseFragments.PublicApi.Tests;

public static class PublicApiCheck
{
    private const string UpdateApprovalsEnvironmentVariable = "SPARSEFRAGMENTS_UPDATE_PUBLIC_API";

    public static void Check<T>() => Check(typeof(T).Assembly, null);

    public static void CheckAssembly(Assembly assembly, string approvalFileName) =>
        Check(assembly, approvalFileName);

    private static void Check(Assembly assembly, string? approvalFileName)
    {
        approvalFileName ??= $"{assembly.GetName().Name!}.approved.txt";
        // Deterministic input order; reflection order is not contractual.
        var publicApi = assembly.GeneratePublicApi(
            new()
            {
                IncludeTypes = assembly
                    .GetExportedTypes()
                    .OrderBy(static type => type.FullName, StringComparer.Ordinal)
                    .ToArray(),
                ExcludeAttributes =
                [
                    typeof(InternalsVisibleToAttribute).FullName!,
                    typeof(TargetFrameworkAttribute).FullName!,
                ],
            }
        );

        if (Environment.GetEnvironmentVariable(UpdateApprovalsEnvironmentVariable) == "1")
        {
            var sourceApproval = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "../../../Approvals", approvalFileName)
            );
            Directory.CreateDirectory(Path.GetDirectoryName(sourceApproval)!);
            File.WriteAllText(
                sourceApproval,
                publicApi,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            );
            return;
        }

        var approvedApi = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Approvals", approvalFileName)
        );
        publicApi.ReplaceLineEndings("\n").ShouldBe(approvedApi.ReplaceLineEndings("\n"));
    }
}

public sealed class PublicApiCheckTest
{
    [Test]
    public void StandaloneFragments() => PublicApiCheck.Check<SparseFragmentModelAttribute>();

    [Test]
    public void StandaloneGenerator() => PublicApiCheck.Check<Generator.SparseFragmentsGenerator>();
}
