using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Text;
using PublicApiGenerator;

namespace SparseFragments.PublicApi.Tests;

public static class PublicApiCheck
{
    private const string UpdateApprovalsEnvironmentVariable = "SPARSEFRAGMENTS_UPDATE_PUBLIC_API";

    public static void Check<T>() => Check(typeof(T).Assembly);

    private static void Check(Assembly assembly)
    {
        var assemblyName = assembly.GetName().Name!;
        var publicApi = assembly.GeneratePublicApi(
            new()
            {
                IncludeTypes = assembly.GetExportedTypes().ToArray(),
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
                Path.Combine(
                    AppContext.BaseDirectory,
                    "../../../Approvals",
                    $"{assemblyName}.approved.txt"
                )
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
            Path.Combine(AppContext.BaseDirectory, "Approvals", $"{assemblyName}.approved.txt")
        );
        publicApi.ReplaceLineEndings("\n").ShouldBe(approvedApi.ReplaceLineEndings("\n"));
    }
}

public sealed class PublicApiCheckTest
{
    [Test]
    public void StandaloneFragments() =>
        PublicApiCheck.Check<SparseFragmentModelAttribute>();

    [Test]
    public void StandaloneGenerator() =>
        PublicApiCheck.Check<Generator.SparseFragmentsGenerator>();
}
