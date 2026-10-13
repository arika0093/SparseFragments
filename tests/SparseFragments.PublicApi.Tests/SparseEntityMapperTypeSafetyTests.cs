using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using SparseFragments;
using SparseFragments.EntityFrameworkCore;

namespace SparseFragments.PublicApi.Tests;

public sealed class SparseEntityMapperTypeSafetyTests
{
    [Test]
    public void ApplyRejectsUntypedAndWrongDtoChangeSetsAtCompileTime()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using SparseFragments;
            using SparseFragments.EntityFrameworkCore;

            public sealed class Entity { }
            public sealed class Dto { }
            public sealed class OtherDto { }

            public static class Consumer
            {
                public static void Valid(
                    SparseEntityMapper<Entity, Dto> mapper,
                    DbContext context,
                    Entity entity,
                    ISparseChangeSet<Dto> changes) =>
                    mapper.Apply(context, entity, changes);

                public static void ValidWithResult(
                    SparseEntityMapper<Entity, Dto> mapper,
                    DbContext context,
                    Entity entity,
                    ISparseChangeSet<Dto> changes) =>
                    mapper.ApplyWithResult(context, entity, changes);

                public static void WrongDto(
                    SparseEntityMapper<Entity, Dto> mapper,
                    DbContext context,
                    Entity entity,
                    ISparseChangeSet<OtherDto> changes) =>
                    mapper.Apply(context, entity, changes);

                public static void WrongDtoWithResult(
                    SparseEntityMapper<Entity, Dto> mapper,
                    DbContext context,
                    Entity entity,
                    ISparseChangeSet<OtherDto> changes) =>
                    mapper.ApplyWithResult(context, entity, changes);

                public static void Untyped(
                    SparseEntityMapper<Entity, Dto> mapper,
                    DbContext context,
                    Entity entity,
                    ISparseChangeSet changes) =>
                    mapper.Apply(context, entity, changes);

                public static void UntypedWithResult(
                    SparseEntityMapper<Entity, Dto> mapper,
                    DbContext context,
                    Entity entity,
                    ISparseChangeSet changes) =>
                    mapper.ApplyWithResult(context, entity, changes);
            }
            """;

        var references = (
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Concat(Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll"))
                .Append(typeof(SparseEntityMapper<,>).Assembly.Location)
                .Append(typeof(ISparseChangeSet).Assembly.Location)
                .Append(typeof(DbContext).Assembly.Location)
        )
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "SparseEntityMapperTypeSafetyProbe",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var errors = compilation
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        errors.Length.ShouldBe(
            4,
            string.Join(Environment.NewLine, errors.Select(static item => item.ToString()))
        );
        errors.ShouldAllBe(static diagnostic => diagnostic.Id == "CS1503");
    }
}
