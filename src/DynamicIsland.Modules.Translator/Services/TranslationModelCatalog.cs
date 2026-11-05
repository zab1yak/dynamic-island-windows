namespace DynamicIsland.Modules.Translator.Services;

internal sealed record TranslationModelFile(string RemoteName, long Size, string Sha256, string? InstalledName = null)
{
    public string LocalName => Path.GetFileName(InstalledName ?? RemoteName);
}

internal sealed record TranslationModel(string Source, string Target, string Repository, string Revision, TranslationModelFile[] Files, string? LanguagePrefix = null, string? UpstreamRepository = null, int BeamWidth = 1, bool SplitSentences = false)
{
    public string Id => Source + "-" + Target;
}

internal static class TranslationModelCatalog
{
    internal static readonly TranslationModel[] Models =
    [
        new("en", "ru", "TigreGotico/opus-mt-tc-big-en-zle-onnx", "5d49b6e3c63c3eddeb6ad2190ec7e2f661b6bd49",
        [
            new("config.json", 1018, "b9c87d0ddf054697332e203e65292e1b413199c08a400c67d2f1c9075bbf28ff"),
            new("int8/decoder_model.onnx", 228889401, "9138d3b26a755bf685cb3424b8c9e739ca8c7306e85fc708edf87ca801724cdf", "decoder_model_quantized.onnx"),
            new("int8/encoder_model.onnx", 140063720, "1361c4babcffa5245986d27a2e67f075132f95c2453b6c56fec1216d6c4d5271", "encoder_model_quantized.onnx"),
            new("source.spm", 802747, "3612abfe04bf08344ba91115f0e15e228a7a15a621ea856bfd548097dbaeb43c"),
            new("target.spm", 1017004, "22940e744b3a9fd166a04880938fb61f7dfa8ba4b5d2d3f6371a6c4ba8f3b019"),
            new("vocab.json", 2510527, "41dbdff4a0b5a6ab125715c3342c5ce6516e93ffd49608813240403f036c5efb"),
        ], ">>rus<<", "Helsinki-NLP/opus-mt-tc-big-en-zle", 4, true),
        new("ru", "en", "TigreGotico/opus-mt-tc-big-zle-en-onnx", "8e66bdc2a181d7f21f65100fd215c7755a6a76b2",
        [
            new("config.json", 1018, "fb12546acae58890669d0ec9f34cb36dcea24a2db49dbc6ab1afcb13bc646ab5"),
            new("int8/decoder_model.onnx", 227740288, "c45b1075a7a18e471cb03dde762bc1cd83c277f4f0a2f452411966f9ab866d32", "decoder_model_quantized.onnx"),
            new("int8/encoder_model.onnx", 139490280, "fde62b663c3bf7ffe62e761b540e10cc4870b7b40ee5545b8df01320140a0a2e", "encoder_model_quantized.onnx"),
            new("source.spm", 1016851, "a982dbb9362861151e36b0db1595b324cd1ce09acf46ce1f4d6d624e11c5807f"),
            new("target.spm", 802387, "e69faed7f1e60eec38c64cceba35cc4fb05ec6478082f4c8a14b141fd6596e9e"),
            new("vocab.json", 2492486, "92148daf001ba378588442dfcdc7c4529210f2f28635ebc092faecf508190680"),
        ], null, "Helsinki-NLP/opus-mt-tc-big-zle-en", 4, true),
    ];

    internal static readonly TranslationModel[] LegacyModels =
    [
        new("en", "ru", "onnx-community/opus-mt-en-ru", "c6967b328d292f9f3d14bab6e9de40f9bd367114",
        [
            new("config.json", 1393, "8da686d7c49cc97f4c11ca17f1b07f9cc859b96e8aec880871d562ebbda458ed"),
            new("onnx/decoder_model_quantized.onnx", 186516257, "cb095b6f25ac8671699600626b8b69684c9777d383c4ac81d0fc3a1d6610b496"),
            new("onnx/encoder_model_quantized.onnx", 51603782, "b8b4f72528c0da92e579af8a739f97fa2f792d73527ba2fee786fa7286c4055b"),
            new("source.spm", 802781, "16bebef1389a0b8ab452772c4e35b9e605e5713f8ac7baa71ca701394eaa086d"),
            new("target.spm", 1080169, "745998e51ba5b058e38b7ac7765c25c43ed5c1c39cc92b27163b9b2e323c9d7c"),
            new("vocab.json", 2726796, "5cf0d95d930d8d3e783c9e2f46a72f08b43a18060dab4ddefbcb66a733efedcb"),
        ]),
        new("ru", "en", "onnx-community/opus-mt-ru-en", "92ef0d550ca96ebd9cd5d13aab6ad41854d99a3d",
        [
            new("config.json", 1393, "bf674cad9714456e2ba2fa886c79dace4c884ba63ed5369f37866cff6164da23"),
            new("onnx/decoder_model_quantized.onnx", 186516257, "29a0c34e0796d01eea5220ff8c4dbe1616f0cf7aa7dfc940de0ca82d3a93fcf3"),
            new("onnx/encoder_model_quantized.onnx", 51603782, "fdd4d1de9cb02feaae8bc892e0e21b1bfd1741fa43a2895d471ee5f53ae260c4"),
            new("source.spm", 1080169, "745998e51ba5b058e38b7ac7765c25c43ed5c1c39cc92b27163b9b2e323c9d7c"),
            new("target.spm", 802781, "16bebef1389a0b8ab452772c4e35b9e605e5713f8ac7baa71ca701394eaa086d"),
            new("vocab.json", 2726796, "5cf0d95d930d8d3e783c9e2f46a72f08b43a18060dab4ddefbcb66a733efedcb"),
        ]),
    ];



    internal static IEnumerable<TranslationModel> AllModels => Models.Concat(LegacyModels);

    internal static TranslationModel? FindInstalled(string source, string target, string path)
    {
        var revision = File.ReadAllText(Path.Combine(path, "installed"));
        return AllModels.FirstOrDefault(m => m.Source == source && m.Target == target && m.Revision == revision);
    }

    internal static TranslationModel? Find(string source, string target) => Models.FirstOrDefault(m => m.Source == source && m.Target == target);
}
