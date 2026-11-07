using Rastro.Extractor.Database;

namespace Rastro.Tests.Extractor;

// Cenários EXT-PGQ-037 em diante: as mensagens de erro dos leitores de SQL (em inglês) viram português.
public class SqlMessageTranslatorTests
{
    [Fact] // EXT-PGQ-037
    public void Translate_ExpectedExpression_VemEmPortugues()
    {
        Assert.Equal("esperava uma expressão, mas encontrou =", SqlMessageTranslator.Translate("Expected an expression, found ="));
        Assert.Equal("esperava o fim da instrução, mas encontrou x", SqlMessageTranslator.Translate("Expected end of statement, found: x"));
    }

    [Fact] // EXT-PGQ-038
    public void Translate_ParentesesEsperados_NomeiaOParentese()
    {
        Assert.Equal("esperava o parêntese de fechamento ')', mas encontrou o fim do texto", SqlMessageTranslator.Translate("Expected ), found: EOF"));
    }

    [Fact] // EXT-PGQ-039
    public void Translate_IncorrectSyntaxNear_VemEmPortugues()
    {
        Assert.Equal("sintaxe incorreta perto de ','", SqlMessageTranslator.Translate("Incorrect syntax near ','."));
        Assert.Equal("sintaxe incorreta perto da palavra-chave 'FROM'", SqlMessageTranslator.Translate("Incorrect syntax near the keyword 'FROM'."));
    }

    [Fact] // EXT-PGQ-040
    public void Translate_AspasSemFechar_VemEmPortugues()
    {
        Assert.Equal("aspas sem fechar depois do texto 'abc'", SqlMessageTranslator.Translate("Unclosed quotation mark after the character string 'abc'."));
        Assert.Equal("texto entre aspas sem fechar", SqlMessageTranslator.Translate("Unterminated string literal"));
    }

    [Fact] // EXT-PGQ-041
    public void Translate_FimInesperadoEParenteses_VemEmPortugues()
    {
        Assert.Equal("o texto terminou antes da hora", SqlMessageTranslator.Translate("Unexpected end of input"));
        Assert.Equal("parênteses sem par", SqlMessageTranslator.Translate("Unbalanced parentheses"));
    }

    [Fact] // EXT-PGQ-042
    public void Translate_MensagemDesconhecida_FicaComOTextoOriginal()
    {
        Assert.Equal("Something else entirely", SqlMessageTranslator.Translate("Something else entirely"));
        Assert.Equal("", SqlMessageTranslator.Translate(""));
    }
}
