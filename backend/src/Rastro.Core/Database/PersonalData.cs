namespace Rastro.Core.Database;

// Coluna com nome de dado pessoal (Fase 38, útil para a LGPD): só pelo nome, nunca pelo conteúdo,
// que o Rastro não lê (ADR-006). O nome é quebrado em palavras ("cpf_cliente", "EmailContato",
// "data_nascimento") e basta uma palavra da lista. "nome" fica de fora de propósito: "produtos.nome"
// não é dado pessoal, e a marca errada ensina a ignorar a marca.
public static class PersonalData
{
    private static readonly HashSet<string> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        "cpf", "cnpj", "rg", "email", "mail", "telefone", "fone", "celular", "phone", "mobile", "whatsapp",
        "nascimento", "birth", "birthday", "birthdate", "dob", "endereco", "address", "logradouro", "cep", "zip", "zipcode",
        "passaporte", "passport", "ssn", "pis", "nis", "senha", "password"
    };

    public static bool IsPersonal(string column) => WordsOf(column).Any(Words.Contains) || column.Contains("e_mail", StringComparison.OrdinalIgnoreCase);

    // "EmailContato" -> email, contato; "data_nascimento" -> data, nascimento.
    private static IEnumerable<string> WordsOf(string name)
    {
        var start = 0;
        for (var i = 1; i <= name.Length; i++)
        {
            var end = i == name.Length || name[i] is '_' or '-' or ' ' || (char.IsUpper(name[i]) && char.IsLower(name[i - 1]));
            if (!end) continue;
            if (i > start) yield return name[start..i].Trim('_', '-', ' ');
            start = i < name.Length && name[i] is '_' or '-' or ' ' ? i + 1 : i;
        }
    }
}
