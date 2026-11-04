namespace DynamicIsland.Modules.Translator.Services;

internal sealed record TranslationModelFile(string RemoteName, long Size, string Sha256)
{
    public string LocalName => Path.GetFileName(RemoteName);
}

internal sealed record TranslationModel(string Source, string Target, string Repository, string Revision, TranslationModelFile[] Files)
{
    public string Id => Source + "-" + Target;
}

internal static class TranslationModelCatalog
{
    internal static readonly TranslationModel[] Models =
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

    internal static TranslationModel? Find(string source, string target) => Models.FirstOrDefault(m => m.Source == source && m.Target == target);
}
