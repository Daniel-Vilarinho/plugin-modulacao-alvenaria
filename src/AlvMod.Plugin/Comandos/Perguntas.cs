using System.Globalization;
using System.Linq;
using AlvMod.Core.Catalogo;
using Autodesk.AutoCAD.EditorInput;

namespace AlvMod.Plugin
{
    /// <summary>Perguntas na linha de comando (questionário, item 65: interface só por linha de comando).</summary>
    internal static class Perguntas
    {
        public static bool? SimNao(Editor ed, string mensagem, bool padraoSim)
        {
            var r = Opcao(ed, mensagem, new[] { "Sim", "Nao" }, padraoSim ? "Sim" : "Nao");
            return r == null ? (bool?)null : r == "Sim";
        }

        /// <summary>Retorna a palavra-chave escolhida, o padrão com Enter, ou null se cancelou (Esc).</summary>
        public static string Opcao(Editor ed, string mensagem, string[] opcoes, string padrao)
        {
            var o = new PromptKeywordOptions(mensagem);
            foreach (var k in opcoes) o.Keywords.Add(k);
            if (padrao != null) o.Keywords.Default = padrao;
            o.AllowNone = padrao != null;
            var r = ed.GetKeywords(o);
            if (r.Status == PromptStatus.None) return padrao;
            return r.Status == PromptStatus.OK ? r.StringResult : null;
        }

        public static double? Numero(Editor ed, string mensagem, double padrao, bool permiteZero = false)
        {
            var o = new PromptDoubleOptions(mensagem)
            {
                AllowNegative = false,
                AllowZero = permiteZero,
                DefaultValue = padrao,
                UseDefaultValue = true
            };
            var r = ed.GetDouble(o);
            return r.Status == PromptStatus.OK ? r.Value : (double?)null;
        }

        public static int? Inteiro(Editor ed, string mensagem, int padrao, int minimo = 0)
        {
            var o = new PromptIntegerOptions(mensagem)
            {
                AllowNegative = minimo < 0,
                AllowZero = minimo <= 0,
                DefaultValue = padrao,
                UseDefaultValue = true
            };
            var r = ed.GetInteger(o);
            return r.Status == PromptStatus.OK ? r.Value : (int?)null;
        }

        /// <summary>Texto com padrão (Enter mantém). Retorna null se cancelou.</summary>
        public static string Texto(Editor ed, string mensagem, string padrao = "")
        {
            var o = new PromptStringOptions(mensagem + (string.IsNullOrEmpty(padrao) ? "" : $" <{padrao}>") + ": ")
            {
                AllowSpaces = true
            };
            var r = ed.GetString(o);
            if (r.Status != PromptStatus.OK) return null;
            return string.IsNullOrWhiteSpace(r.StringResult) ? padrao : r.StringResult.Trim();
        }

        /// <summary>Lista as famílias numeradas e pede o número.</summary>
        public static Familia EscolherFamilia(Editor ed, CatalogoBlocos catalogo, string atual)
        {
            ed.WriteMessage("\nFamílias cadastradas:");
            for (int i = 0; i < catalogo.Familias.Count; i++)
            {
                var f = catalogo.Familias[i];
                ed.WriteMessage($"\n  {i + 1}. {f.Nome} ({f.Material}) — peças: " +
                                string.Join(", ", f.Pecas.Where(p => p.Funcao != FuncaoPeca.Canaleta).Select(p => p.Codigo)));
            }
            int padrao = System.Math.Max(1, catalogo.Familias.FindIndex(f => f.Nome == atual) + 1);
            var n = Inteiro(ed, "\nNúmero da família", padrao, 1);
            if (n == null) return null;
            if (n < 1 || n > catalogo.Familias.Count)
            {
                ed.WriteMessage("\nNúmero inválido.");
                return null;
            }
            return catalogo.Familias[n.Value - 1];
        }

        public static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
