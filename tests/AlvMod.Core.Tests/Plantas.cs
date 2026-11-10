using System;
using System.Collections.Generic;
using System.Linq;
using AlvMod.Core.Catalogo;
using AlvMod.Core.Modulacao;
using AlvMod.Core.Planta;

namespace AlvMod.Core.Tests
{
    /// <summary>
    /// Plantas sintéticas desenhadas à mão (cm), com eixos na grade de 5 cm como numa arquitetura modulada:
    /// paredes de 15 cm pelas duas faces, portas com marco e arco, janelas com vidro.
    /// </summary>
    internal static class Plantas
    {
        public const string Parede = "PAREDE", Porta = "PORTA", Janela = "JANELA";

        public static OpcoesDeteccao Opcoes() => new OpcoesDeteccao
        {
            LayersParede = { Parede }, LayersPorta = { Porta }, LayersJanela = { Janela }
        };

        public static void Linha(List<Segmento> s, string layer, double x1, double y1, double x2, double y2) =>
            s.Add(new Segmento(layer, x1, y1, x2, y2));

        /// <summary>Arco de porta (duas cordas com raio e centro), centro na dobradiça.</summary>
        public static void Arco(List<Segmento> s, double cx, double cy, double r, double a0, double a1)
        {
            double am = (a0 + a1) / 2;
            (double, double) P(double a) => (cx + r * Math.Cos(a), cy + r * Math.Sin(a));
            var (x0, y0) = P(a0); var (xm, ym) = P(am); var (x1, y1) = P(a1);
            s.Add(new Segmento(Porta, x0, y0, xm, ym) { Raio = r, Cx = cx, Cy = cy });
            s.Add(new Segmento(Porta, xm, ym, x1, y1) { Raio = r, Cx = cx, Cy = cy });
        }

        /// <summary>
        /// Sala 400 × 300 (faces externas), parede interna em x = 195..210 (dois T), porta de 80 cm na parede
        /// de baixo (x 60..140, dobradiça à esquerda) e janela de 100 cm na de cima (x 250..350).
        /// Eixos: x 7,5 / 202,5 / 392,5 e y 7,5 / 292,5 — distâncias múltiplas de 5.
        /// </summary>
        public static List<Segmento> Sala()
        {
            var s = new List<Segmento>();
            // Externas
            Linha(s, Parede, 0, 0, 60, 0); Linha(s, Parede, 140, 0, 400, 0);
            Linha(s, Parede, 400, 0, 400, 300);
            Linha(s, Parede, 400, 300, 350, 300); Linha(s, Parede, 250, 300, 0, 300);
            Linha(s, Parede, 0, 300, 0, 0);
            // Internas
            Linha(s, Parede, 15, 15, 60, 15); Linha(s, Parede, 140, 15, 195, 15); Linha(s, Parede, 210, 15, 385, 15);
            Linha(s, Parede, 385, 15, 385, 285);
            Linha(s, Parede, 385, 285, 350, 285); Linha(s, Parede, 250, 285, 210, 285); Linha(s, Parede, 195, 285, 15, 285);
            Linha(s, Parede, 15, 285, 15, 15);
            // Parede interna
            Linha(s, Parede, 195, 15, 195, 285); Linha(s, Parede, 210, 15, 210, 285);
            // Porta: marcos e arco (dobradiça em x = 62)
            Linha(s, Porta, 60, 0, 60, 15); Linha(s, Porta, 140, 0, 140, 15);
            Arco(s, 62, 15, 75, 0, Math.PI / 2);
            // Janela: vidro
            Linha(s, Janela, 250, 289, 350, 289); Linha(s, Janela, 250, 296, 350, 296);
            return s;
        }

        /// <summary>
        /// Dois cômodos espelhados (eixo de simetria x = 402,5): a sala da esquerda com porta na parede de
        /// baixo e o espelho dela à direita; parede do meio em x = 395..410. Eixos 7,5 / 402,5 / 797,5.
        /// </summary>
        public static List<Segmento> Espelhada()
        {
            var esq = new List<Segmento>();
            Linha(esq, Parede, 0, 0, 60, 0); Linha(esq, Parede, 140, 0, 395, 0);
            Linha(esq, Parede, 0, 300, 395, 300); Linha(esq, Parede, 0, 300, 0, 0);
            Linha(esq, Parede, 15, 15, 60, 15); Linha(esq, Parede, 140, 15, 395, 15);
            Linha(esq, Parede, 15, 285, 395, 285); Linha(esq, Parede, 15, 285, 15, 15);
            Linha(esq, Parede, 395, 15, 395, 285);
            Linha(esq, Porta, 60, 0, 60, 15); Linha(esq, Porta, 140, 0, 140, 15);
            Arco(esq, 62, 15, 75, 0, Math.PI / 2);
            var s = new List<Segmento>(esq);
            foreach (var x in esq)
                s.Add(new Segmento(x.Layer, 805 - x.X1, x.Y1, 805 - x.X2, x.Y2)
                    { Raio = x.Raio, Cx = x.Raio > 0 ? 805 - x.Cx : 0, Cy = x.Cy });
            Linha(s, Parede, 395, 0, 410, 0); Linha(s, Parede, 395, 300, 410, 300);
            return s;
        }

        public static PlantaDetectada Detectar(List<Segmento> segmentos, OpcoesDeteccao op = null)
        {
            var p = DetectorParedes.Detectar(segmentos, op ?? Opcoes());
            GradeModular.Ajustar(p);
            Numeracao.Numerar(p);
            return p;
        }

        public static (SolucaoPavimento sol, ModulacaoFiadas mod) Modular(PlantaDetectada p)
        {
            var familia = CatalogoPadrao.Familia39Concreto();
            var m = new Modulador(familia);
            var op = new OpcoesFiadas { BlocoEncontro = familia.Buscar("54") };
            var sol = PlanejadorFiadas.Resolver(p, m, op);
            return (sol, ModulacaoPavimento.Modular(sol, m, op));
        }

        /// <summary>Retângulos (cm) das peças de uma fiada, incluindo os 54 fixos.</summary>
        public static List<(string codigo, double x0, double y0, double x1, double y1, Parede parede)> Pecas(
            SolucaoPavimento sol, ModulacaoFiadas mod, int fiada)
        {
            var r = new List<(string, double, double, double, double, Parede)>();
            void Add(Parede w, double a, double b, string c) =>
                r.Add(w.Horizontal ? (c, a, w.Coord - 7, b, w.Coord + 7, w) : (c, w.Coord - 7, a, w.Coord + 7, b, w));
            foreach (var (t, res) in mod.Fiada(fiada))
                foreach (var p in res.Pecas) Add(t.Parede, t.Inicio + p.Inicio, t.Inicio + p.Fim, p.Peca.Codigo);
            foreach (var f in sol.Plano(fiada).Fixas) Add(f.Parede, f.Inicio, f.Fim, f.Peca.Codigo);
            return r;
        }

        /// <summary>Menor boneca de porta do pavimento (onde ela cabe dos dois lados): da face da parede que encontra a da porta até o vão (cm).</summary>
        internal static double MenorBoneca(PlantaDetectada p, OpcoesFiadas op)
        {
            double menor = double.MaxValue;
            foreach (var w in p.Paredes)
                foreach (var e in p.EncontrosDe(w))
                {
                    double t = w.AoLongo(e.X, e.Y);
                    foreach (var v in w.VaosQueCortam().Where(v => v.Tipo == TipoVao.Porta))
                    {
                        var (a, b) = v.Alvenaria(op.FolgaPorta);
                        // Parede curta demais para ter boneca dos dois lados (ex.: 110 cm com porta de 91): não conta.
                        if (w.Comprimento - op.Largura < b - a + 2 * op.BonecaMinima) continue;
                        double boneca = b <= t ? t - op.Largura / 2 - b : a - (t + op.Largura / 2);
                        if (boneca < 60) menor = Math.Min(menor, boneca);
                    }
                }
            return menor;
        }
    }
}
