using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AlvMod.Core.Catalogo;

namespace AlvMod.Core.Saidas
{
    public sealed class LinhaQuantitativo
    {
        public string Bloco { get; set; }
        public Peca Peca { get; set; }
        public string Familia { get; set; }
        public int Quantidade { get; set; }
        public double? PrecoUnitario { get; set; }
        public double? Total => PrecoUnitario * Quantidade;
    }

    /// <summary>Contagem de blocos por nome de bloco DWG, exportada em CSV para o Excel (questionário, item 49).</summary>
    public static class Quantitativo
    {
        public static List<LinhaQuantitativo> Contar(IEnumerable<string> nomesBlocos, CatalogoBlocos catalogo,
            IDictionary<string, double> precos = null)
        {
            return nomesBlocos
                .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var familia = catalogo.Familias.FirstOrDefault(f => f.BuscarPorBloco(g.Key) != null);
                    var peca = familia?.BuscarPorBloco(g.Key);
                    double preco = 0;
                    bool temPreco = peca != null && precos != null && precos.TryGetValue(peca.Codigo, out preco);
                    return new LinhaQuantitativo
                    {
                        Bloco = g.Key,
                        Peca = peca,
                        Familia = familia?.Nome,
                        Quantidade = g.Count(),
                        PrecoUnitario = temPreco ? preco : (double?)null
                    };
                })
                .OrderBy(l => l.Peca == null)
                .ThenByDescending(l => l.Peca?.Comprimento ?? 0)
                .ThenBy(l => l.Bloco)
                .ToList();
        }

        /// <summary>CSV no padrão do Excel em português: separador ";" e vírgula decimal, UTF-8 com BOM.</summary>
        public static byte[] ParaCsv(List<LinhaQuantitativo> linhas, string titulo)
        {
            var br = new CultureInfo("pt-BR");
            var sb = new StringBuilder();
            sb.AppendLine(Celula(titulo));
            sb.AppendLine("Bloco DWG;Família;Peça;Dimensões (cm);Função;Quantidade;Preço unitário;Total");
            foreach (var l in linhas)
            {
                sb.AppendLine(string.Join(";",
                    Celula(l.Bloco),
                    Celula(l.Familia ?? "(fora do catálogo)"),
                    Celula(l.Peca?.Codigo ?? ""),
                    Celula(l.Peca?.Dimensoes ?? ""),
                    Celula(l.Peca?.Funcao.ToString() ?? ""),
                    l.Quantidade.ToString(br),
                    l.PrecoUnitario?.ToString("0.00", br) ?? "",
                    l.Total?.ToString("0.00", br) ?? ""));
            }
            sb.AppendLine(string.Join(";", "TOTAL", "", "", "", "",
                linhas.Sum(l => l.Quantidade).ToString(br), "",
                linhas.Any(l => l.Total.HasValue) ? linhas.Sum(l => l.Total ?? 0).ToString("0.00", br) : ""));

            var bom = Encoding.UTF8.GetPreamble();
            var corpo = Encoding.UTF8.GetBytes(sb.ToString());
            return bom.Concat(corpo).ToArray();
        }

        private static string Celula(string s) =>
            s.IndexOfAny(new[] { ';', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}
