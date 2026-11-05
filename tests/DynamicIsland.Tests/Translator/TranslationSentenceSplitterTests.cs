using DynamicIsland.Modules.Translator.Services;

namespace DynamicIsland.Tests.Translator;

public sealed class TranslationSentenceSplitterTests
{
    [Fact]
    public void SplitsCompleteBilingualSentencesWithoutDroppingQuestions()
    {
        Assert.Equal(["You look upset.", "What's up?"], TranslationSentenceSplitter.Split("You look upset. What's up?"));
        Assert.Equal(["Ты серьёзно?", "Я думал, ты шутишь."], TranslationSentenceSplitter.Split("Ты серьёзно? Я думал, ты шутишь."));
    }

    [Fact]
    public void KeepsDecimalVersionAndAbbreviatedNamesIntact()
    {
        Assert.Equal(["Dr. Smith uses version 0.3.1.", "It works."], TranslationSentenceSplitter.Split("Dr. Smith uses version 0.3.1. It works."));
        Assert.Equal(["A. B. Smith arrived.", "We left."], TranslationSentenceSplitter.Split("A. B. Smith arrived. We left."));
    }

    [Fact]
    public void PreservesClosingQuotesAndRepeatedPunctuation()
    {
        Assert.Equal(["He said \"Stop!\"", "Then he left."], TranslationSentenceSplitter.Split("He said \"Stop!\" Then he left."));
        Assert.Equal(["Really?!", "yes."], TranslationSentenceSplitter.Split("Really?! yes."));
    }

    [Fact]
    public void PreservesAllNonWhitespaceInputAndDoesNotInventEmptySegments()
    {
        const string input = "First sentence.\n\nВторое предложение!  Third?";
        var segments = TranslationSentenceSplitter.Split(input);
        Assert.Equal(3, segments.Length);
        Assert.Equal(string.Concat(input.Where(c => !char.IsWhiteSpace(c))), string.Concat(string.Concat(segments).Where(c => !char.IsWhiteSpace(c))));
    }
}
