(() => {
    const configure = monaco => {
        const languageId = "playground-csharp";
        if (!monaco.languages.getLanguages().some(language => language.id === languageId)) {
            monaco.languages.register({ id: languageId });
        }

        monaco.languages.setMonarchTokensProvider(languageId, {
            defaultToken: "",
            tokenPostfix: ".cs",
            keywords: [
                "abstract", "as", "async", "await", "base", "break", "case", "catch", "checked",
                "class", "const", "continue", "default", "delegate", "do", "else", "enum", "event",
                "explicit", "extern", "false", "finally", "fixed", "for", "foreach", "goto", "if",
                "implicit", "in", "interface", "internal", "is", "lock", "namespace", "new", "null",
                "operator", "out", "override", "params", "partial", "private", "protected", "public",
                "readonly", "ref", "return", "sealed", "sizeof", "stackalloc", "static", "struct",
                "switch", "this", "throw", "true", "try", "typeof", "unchecked", "unsafe", "using",
                "var", "virtual", "volatile", "while", "record", "init", "get", "set", "with", "yield",
                "where", "when", "select", "from", "join", "on", "equals", "group", "by", "into",
                "let", "orderby", "ascending", "descending"
            ],
            typeKeywords: [
                "bool", "byte", "char", "decimal", "double", "float", "int", "long", "object",
                "sbyte", "short", "string", "uint", "ulong", "ushort", "void", "dynamic"
            ],
            operators: [
                "=", ">", "<", "!", "~", "?", ":", "==", "<=", ">=", "!=", "&&", "||", "++", "--",
                "+", "-", "*", "/", "&", "|", "^", "%", "<<", ">>", ">>>", "+=", "-=", "*=", "/=",
                "&=", "|=", "^=", "%=", "<<=", ">>=", "??", "??=", "?.", "=>", ".."
            ],
            symbols: /[=><!~?:&|+\-*\/^%]+/,
            tokenizer: {
                root: [
                    [/#.*$/, "keyword.directive"],
                    [/\/\/.*$/, "comment"],
                    [/\/\*/, "comment", "@comment"],
                    [/"""/, "string", "@rawString"],
                    [/\$?@?"|@\$"/, "string", "@string"],
                    [/'([^'\\]|\\.)*'?/, "string"],
                    [/\b(new)([ \t]+)([A-Z][A-Za-z0-9_]*(?:\.[A-Z][A-Za-z0-9_]*)*)/, ["keyword", "white", "type.identifier"]],
                    [/\bnew\b/, "keyword"],
                    [/\b(class|interface|struct|enum|record|delegate)(\s+)([A-Za-z_]\w*)/, ["keyword", "white", "type.identifier"]],
                    [/[A-Z][A-Za-z0-9_]*(?=\s*\])/, "type.identifier"],
                    [/[A-Z][A-Za-z0-9_]*(?=\s*<)/, "type.identifier"],
                    [/[A-Z][A-Za-z0-9_]*(?=\s*[>,?])/, "type.identifier"],
                    // Colors the return type of body-less `...;` declarations,
                    // which no lookahead above covers.
                    [/\b([A-Z][A-Za-z0-9_.]*(?:<[^(){}]*>)?(?:\[\])*\?*)(\s+)([A-Za-z_]\w*)(?=\s*\()/,
                        ["type.identifier", "white", "function"]],
                    // Field/property/parameter declaration `TypeName name`:
                    // colors the type.
                    [/\b([A-Z][A-Za-z0-9_.]*(?:<[^(){}]*>)?(?:\[\])*\?*)(\s+)([_A-Za-z]\w*)/,
                        ["type.identifier", "white", "identifier"]],
                    [/[A-Za-z_]\w*(?=\s*\()/, {
                        cases: {
                            "@keywords": "keyword",
                            "@typeKeywords": "type",
                            "@default": "function"
                        }
                    }],
                    [/[A-Za-z_]\w*/, {
                        cases: {
                            "@keywords": "keyword",
                            "@typeKeywords": "type",
                            "@default": "identifier"
                        }
                    }],
                    [/\d+\.\d+([eE][\-+]?\d+)?[fFdDmM]?/, "number.float"],
                    [/0[xX][0-9a-fA-F_]+[uUlL]*/, "number.hex"],
                    [/\d[\d_]*([uUlL]+)?/, "number"],
                    [/@[A-Za-z_]\w*/, "identifier"],
                    [/@symbols/, {
                        cases: {
                            "@operators": "operator",
                            "@default": ""
                        }
                    }],
                    [/[{}()\[\]]/, "@brackets"],
                    [/\./, "delimiter"],
                    [/[;,]/, "delimiter"],
                    [/[ \t\r\n]+/, "white"]
                ],
                comment: [
                    [/[^/*]+/, "comment"],
                    [/\*\//, "comment", "@pop"],
                    [/[/*]/, "comment"]
                ],
                string: [
                    [/[^\\"]+/, "string"],
                    [/\\./, "string.escape"],
                    [/"/, "string", "@pop"]
                ],
                rawString: [
                    [/[^"]+/, "string"],
                    [/"""/, "string", "@pop"],
                    [/"/, "string"]
                ]
            }
        });

        monaco.editor.defineTheme("pg-light", {
            base: "vs",
            inherit: true,
            rules: [
                { token: "type.identifier", foreground: "267F99" },
                { token: "function", foreground: "795E26" },
                { token: "keyword.directive", foreground: "800000" },
                { token: "number.hex", foreground: "098658" }
            ],
            colors: {}
        });
        monaco.editor.defineTheme("pg-dark", {
            base: "vs-dark",
            inherit: true,
            rules: [
                { token: "type.identifier", foreground: "4EC9B0" },
                { token: "function", foreground: "DCDCAA" },
                { token: "keyword.directive", foreground: "C586C0" },
                { token: "number.hex", foreground: "B5CEA8" }
            ],
            colors: {}
        });
    };

    let attempts = 0;
    const configureWhenReady = () => {
        const monaco = window.monaco;
        if (monaco?.languages && monaco.editor) {
            configure(monaco);
            return;
        }
        if (attempts++ >= 400) {
            throw new Error("Monaco did not become available for playground language setup.");
        }
        window.setTimeout(configureWhenReady, 25);
    };

    configureWhenReady();
})();
