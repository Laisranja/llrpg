using System;
using UnityEngine;

public enum NpcIntent { Neutro = 0, Alerta = 1, Missao = 2 }

/// Resposta estruturada do LLM. JsonUtility nao aceita acento em nome de campo,
/// por isso o campo e "intencao" (sem cedilha/til).
[Serializable]
public sealed class NpcReply
{
    public string fala, intencao, humor;

    public static readonly string[] Intents = { "neutro", "alerta", "missao" };
    public static readonly string[] Moods = { "calmo", "desconfiado", "irritado", "animado" };

    public NpcIntent Intent => (NpcIntent)Array.IndexOf(Intents, intencao);

    /// Extrai e valida. Se retornar false, NADA no jogo deve mudar.
    public static bool TryParse(string raw, out NpcReply reply, out string error)
    {
        reply = null; error = null;
        if (string.IsNullOrWhiteSpace(raw)) { error = "vazio"; return false; }

        // tolera cercas de markdown / texto ao redor: pega do primeiro { ao ultimo }
        int a = raw.IndexOf('{'), b = raw.LastIndexOf('}');
        if (a < 0 || b <= a) { error = "sem JSON"; return false; }

        NpcReply r;
        try { r = JsonUtility.FromJson<NpcReply>(raw.Substring(a, b - a + 1)); }
        catch (ArgumentException e) { error = "JSON quebrado: " + e.Message; return false; }
        if (r == null) { error = "JSON nulo"; return false; }

        r.fala = r.fala?.Trim();
        r.intencao = r.intencao?.Trim().ToLowerInvariant();
        r.humor = r.humor?.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(r.fala) || r.fala.Length > 400) { error = "fala invalida"; return false; }
        if (Array.IndexOf(Intents, r.intencao) < 0) { error = "intencao invalida: " + r.intencao; return false; }
        if (Array.IndexOf(Moods, r.humor) < 0) { error = "humor invalido: " + r.humor; return false; }

        reply = r;
        return true;
    }
}
