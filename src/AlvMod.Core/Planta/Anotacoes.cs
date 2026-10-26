using System;
using System.Collections.Generic;
using System.Linq;

namespace AlvMod.Core.Planta
{
    /// <summary>Uma linha de cota em cadeia ao longo da face de uma parede, dentro de um cômodo.</summary>
    public sealed class LinhaCota
    {
        public Parede Parede { get; set; }

        /// <summary>Lado da face cotada: +1 = coordenada maior (y+ nas horizontais, x+ nas verticais), −1 = menor.</summary>
        public int Lado { get; set; }

        /// <summary>Posições ao longo do eixo (cm), em ordem: faces das paredes e bordas das aberturas.</summary>
        public List<double> Pontos { get; set; } = new List<double>();

        /// <summary>Distância (cm) da face da parede até a linha de cota, para dentro do cômodo.</summary>
        public double Afastamento { get; set; }
    }

    /// <summary>
    /// Anotações da planta de modulação, como o engenheiro faz: cotas internas por cômodo (face a face dos blocos,
    /// com as aberturas no meio: "40 | 151 | 45" e o total "236"), e de que lado de cada parede fica o cômodo.
    /// </summary>
    public static class Anotacoes
    {
        /// <param name="afastamento">Distância da cota em cadeia até a face (cm); o total vai no dobro.</param>
        public static List<LinhaCota> CotasInternas(PlantaDetectada planta, OpcoesFiadas op, double afastamento = 35)
        {
            var linhas = new List<LinhaCota>();
            double meia = op.Largura / 2;
            foreach (var w in planta.Paredes)
            {
                foreach (int lado in new[] { -1, 1 })
                {
                    double face = w.Coord + lado * meia;
                    // Paredes perpendiculares que encostam nesta face (ou a atravessam) cortam a face em pedaços.
                    var cortes = planta.Paredes
                        .Where(p => p.Orientacao != w.Orientacao && p.Coord >= w.Inicio - 1 && p.Coord <= w.Fim + 1 &&
                                    (lado > 0 ? p.Fim > face + 1 && p.Inicio <= face + 1 : p.Inicio < face - 1 && p.Fim >= face - 1))
                        .Select(p => (a: p.Coord - meia, b: p.Coord + meia))
                        .OrderBy(c => c.a)
                        .ToList();
                    var pedacos = new List<(double a, double b)>();
                    double cursor = w.Inicio;
                    foreach (var (a, b) in cortes)
                    {
                        if (a > cursor) pedacos.Add((cursor, a));
                        cursor = Math.Max(cursor, b);
                    }
                    if (cursor < w.Fim) pedacos.Add((cursor, w.Fim));

                    foreach (var (a, b) in pedacos)
                    {
                        if (b - a < 20) continue;
                        double meio = (a + b) / 2;
                        if (Exterior(planta, w, lado, meio)) continue;
                        var pts = new List<double> { a, b };
                        foreach (var v in w.Vaos)
                        {
                            var (va, vb) = v.Alvenaria(v.Tipo == TipoVao.Porta ? op.FolgaPorta : v.Tipo == TipoVao.Janela ? op.FolgaJanela : 0);
                            if (va > a + 0.5 && va < b - 0.5) pts.Add(va);
                            if (vb > a + 0.5 && vb < b - 0.5) pts.Add(vb);
                        }
                        pts = pts.Distinct().OrderBy(x => x).ToList();
                        linhas.Add(new LinhaCota { Parede = w, Lado = lado, Pontos = pts, Afastamento = afastamento });
                        if (pts.Count > 2)
                            linhas.Add(new LinhaCota { Parede = w, Lado = lado, Pontos = new List<double> { a, b }, Afastamento = 2 * afastamento });
                    }
                }
            }
            return linhas;
        }

        /// <summary>
        /// A face de um lado da parede dá para fora do prédio? (nenhuma parede paralela daquele lado cobrindo
        /// a posição <paramref name="t"/> ao longo do eixo.)
        /// </summary>
        public static bool Exterior(PlantaDetectada planta, Parede w, int lado, double t) =>
            !planta.Paredes.Any(p => p != w && p.Orientacao == w.Orientacao &&
                                     (lado > 0 ? p.Coord > w.Coord + 1 : p.Coord < w.Coord - 1) &&
                                     p.Inicio - 1 <= t && t <= p.Fim + 1);

        /// <summary>Lado da parede onde vai o nome dela: o de dentro do prédio (nas internas, o de cima/direita).</summary>
        public static int LadoDoRotulo(PlantaDetectada planta, Parede w)
        {
            double meio = (w.Inicio + w.Fim) / 2;
            if (Exterior(planta, w, 1, meio)) return -1;
            return 1;
        }
    }
}
