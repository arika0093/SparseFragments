using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseFragments.Generator.Shared;

/// <summary>
/// Emits the nested bindable <c>Observable</c> proxy for a generated model.
/// </summary>
/// <remarks>
/// Product-neutral Shared implementation: only BCL ComponentModel contracts plus
/// ordinary equality primitives, so both SparseFragments and downstream generators
/// (e.g. Configlue, which compiles these Shared sources) expose the same surface.
/// The proxy wraps the live model instance; no state is copied except where value
/// semantics require it. Collections are replace-only for notification purposes.
/// </remarks>
internal static class SparseObservableEmitter
{
    public static string ObservableTypeName(ImmutableArray<SparseMemberModel> members)
    {
        var taken = new HashSet<string>(members.Select(static member => member.Property.Name));
        var builder = new StringBuilder("Observable");
        while (taken.Contains(builder.ToString()))
        {
            builder.Insert(0, "Sparse");
        }

        return builder.ToString();
    }

    public static void AppendObservable(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members
    )
    {
        var observable = ObservableTypeName(members);
        code.AppendLineAt(
            1,
            "/// <summary>Bindable proxy over the live model instance. No state is copied.</summary>"
        );
        code.AppendLineAt(
            1,
            "public sealed class "
                + observable
                + " : global::System.ComponentModel.INotifyPropertyChanged"
        );
        code.AppendLineAt(1, "{");
        code.AppendLineAt(2, "private readonly " + modelType + " __model;");
        code.AppendLineAt(2, "private readonly global::System.Action? __onChanged;");
        foreach (var member in members)
        {
            if (
                member.ChildModel is not null
                && member.ChildIsReferenceType
                && member.Property.Name != "PropertyChanged"
            )
            {
                var childObservable = ChildObservableType(member);
                code.AppendLineAt(
                    2,
                    "private "
                        + member.ChildModel.Value.NonNullableName
                        + "? __target_"
                        + member.Id
                        + ";"
                );
                code.AppendLineAt(2, "private " + childObservable + "? __proxy_" + member.Id + ";");
            }
        }

        code.AppendLineAt(
            2,
            "public "
                + observable
                + "("
                + modelType
                + " value, global::System.Action? onChanged = null)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if ((object?)value is null) throw new global::System.ArgumentNullException(nameof(value));"
        );
        code.AppendLineAt(3, "__model = value;");
        code.AppendLineAt(3, "__onChanged = onChanged;");
        code.AppendLineAt(2, "}");
        if (!members.Any(static member => member.Property.Name == "Model"))
        {
            code.AppendLineAt(2, "public " + modelType + " Model => __model;");
        }

        code.AppendLineAt(2, "internal " + modelType + " __SparseTarget => __model;");
        code.AppendLineAt(
            2,
            "public event global::System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;"
        );
        foreach (var member in members)
        {
            if (member.Property.Name == "PropertyChanged")
            {
                // The INotifyPropertyChanged event occupies this name; the member is
                // intentionally not proxied to keep the generated surface compilable.
                continue;
            }

            AppendMember(code, member, modelType);
        }

        code.AppendLineAt(
            2,
            "private void __Raise(string propertyName) => PropertyChanged?.Invoke(this, new global::System.ComponentModel.PropertyChangedEventArgs(propertyName));"
        );
        code.AppendLineAt(1, "}");
    }

    private static string ChildObservableType(SparseMemberModel member)
    {
        var fragment = member.ChildFragmentType!;
        return fragment.Substring(0, fragment.Length - "Fragment".Length) + "Observable";
    }

    private static void AppendMember(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string modelType
    )
    {
        _ = modelType;
        var name = SparseNaming.EscapeIdentifier(member.Property.Name);
        var literal = SymbolDisplay.FormatLiteral(member.Property.Name, true);
        var canWrite = !member.Property.IsReadOnly && !member.Property.IsInitOnly;
        if (member.ChildModel is not null && member.ChildIsReferenceType)
        {
            AppendReferenceChild(code, member, name, literal, canWrite);
            return;
        }

        var type = member.Property.Type.Name;
        var comparer = "global::System.Collections.Generic.EqualityComparer<" + type + ">.Default";
        code.AppendLineAt(2, "public " + type + " " + name);
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "get => __model." + name + ";");
        if (canWrite)
        {
            code.AppendLineAt(3, "set");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(
                4,
                "if (" + comparer + ".Equals(__model." + name + ", value)) return;"
            );
            code.AppendLineAt(4, "__model." + name + " = value;");
            code.AppendLineAt(4, "__Raise(" + literal + ");");
            code.AppendLineAt(4, "if (__onChanged is not null) __onChanged();");
            code.AppendLineAt(3, "}");
        }

        code.AppendLineAt(2, "}");
    }

    private static void AppendReferenceChild(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string name,
        string literal,
        bool canWrite
    )
    {
        var childModel = member.ChildModel!.Value.NonNullableName;
        var childObservable = ChildObservableType(member);
        // The property is observable-typed so nested bindings observe changes; the
        // setter unwraps back to the model type for replacement.
        code.AppendLineAt(2, "public " + childObservable + "? " + name);
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "get");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var current = __model." + name + ";");
        code.AppendLineAt(
            4,
            "if ((object?)current is null) { __target_"
                + member.Id
                + " = null; __proxy_"
                + member.Id
                + " = null; return null; }"
        );
        code.AppendLineAt(
            4,
            "if (!global::System.Object.ReferenceEquals(__target_"
                + member.Id
                + ", current)) { __target_"
                + member.Id
                + " = current; __proxy_"
                + member.Id
                + " = new "
                + childObservable
                + "(current, () => { __Raise("
                + literal
                + "); if (__onChanged is not null) __onChanged(); }); }"
        );
        code.AppendLineAt(4, "return __proxy_" + member.Id + ";");
        code.AppendLineAt(3, "}");
        if (canWrite)
        {
            code.AppendLineAt(3, "set");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(4, "var next = value is null ? null : value.__SparseTarget;");
            code.AppendLineAt(
                4,
                "if (global::System.Object.ReferenceEquals(__model." + name + ", next)) return;"
            );
            code.AppendLineAt(4, "__model." + name + " = (" + childModel + ")next!;");
            code.AppendLineAt(4, "__target_" + member.Id + " = null;");
            code.AppendLineAt(4, "__proxy_" + member.Id + " = null;");
            code.AppendLineAt(4, "__Raise(" + literal + ");");
            code.AppendLineAt(4, "if (__onChanged is not null) __onChanged();");
            code.AppendLineAt(3, "}");
        }

        code.AppendLineAt(2, "}");
    }
}
