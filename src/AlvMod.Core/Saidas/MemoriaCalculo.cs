using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AlvMod.Core.Modulacao;

namespace AlvMod.Core.Saidas
{
    /// <summary>Uma linha da memória de cálculo: o que o plugin fez num trecho de parede.</summary>
    public sealed class LinhaMemoria
    {
        public string Comando { get; set; }
        public string Parede { get; set; }
        public int Fiada { get; set; }

        /// <summary>Comprimento do trecho de alvenaria (cm).</summary>
        public double Comprimento { get; set; }

        /// <summary>O que há nas pontas (ex.: "porta | canto, passa (34)").</summary>
        public string Pontas { get; set; }

        /// <summary>Peças em ordem, do início ao fim do trecho (ex.: "34 + 39 × 5 + 19").</summary>
        public string Pecas { get; set; }

        /// <summary>"fechou", "parcial (sobra 2,5 cm)" ou "sem solução".</summary>
        public string Situacao { get; set; }

        public string Observacoes { get; set; }
    }

    /// <summary>
    /// Memória de cálculo da modulação, para o projetista auditar parede por parede o que foi colocado e
    /// por quê (vai no ALVRELATORIO, depois das ocorrências).
    /// </summary>
    public static class MemoriaCalculo
    {
        private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

        public static LinhaMemoria Linha(string comando, string parede, int fiada, double comprimento, Pontas pontas,
            ResultadoModulacao r) => new LinhaMemoria
        {
            Comando = comando,
            Parede = parede,
            Fiada = fiada,
            Comprimento = comprimento,
            Pontas = DescreverPontas(pontas),
            Pecas = DescreverPecas(r.Pecas.Select(p => p.Peca.Codigo)),
            Situacao = r.Sucesso ? "fechou"
                     : r.Parcial ? $"parcial (sobra {r.Sobra.ToString("0.#", PtBr)} cm {(r.SobraNoInicio ? "no início" : "no fim")})"
                     : "sem solução",
            Observacoes = string.Join("; ", r.Avisos.Concat(r.Sugestoes.Count > 0 && !r.Sucesso
                ? new[] { "fecharia com " + string.Join(" ou ", r.Sugestoes) + " cm" }
                : new string[0]))
        };

        /// <summary>Peças em ordem, agrupando as repetidas seguidas: "34 + 39 × 5 + 19".</summary>
        public static string DescreverPecas(IEnumerable<string> codigos)
        {
            var grupos = new List<(string codigo, int n)>();
            foreach (var c in codigos)
            {
                if (grupos.Count > 0 && grupos[grupos.Count - 1].codigo == c)
                    grupos[grupos.Count - 1] = (c, grupos[grupos.Count - 1].n + 1);
                else
                    grupos.Add((c, 1));
            }
            return grupos.Count == 0 ? "—" : string.Join(" + ", grupos.Select(g => g.n > 1 ? $"{g.codigo} × {g.n}" : g.codigo));
        }

        /// <summary>O que há no início e no fim do trecho: "porta | canto/T, passa (34)".</summary>
        public static string DescreverPontas(Pontas p)
        {
            p = p ?? Pontas.Nenhuma;
            string Lado(Bordas b)
            {
                if (p.Porta.HasFlag(b)) return "porta";
                if (p.Passa34.HasFlag(b)) return "encontro, passa (34)";
                if (p.Exige39.HasFlag(b)) return "encontro, encosta (39)";
                if (p.Encontro.HasFlag(b)) return "encontro";
                return "livre";
            }
            return Lado(Bordas.Inicio) + " | " + Lado(Bordas.Fim);
        }
    }
}
