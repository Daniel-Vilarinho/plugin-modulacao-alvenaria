using System;
using System.Collections.Generic;
using System.Linq;
using AlvMod.Core.Catalogo;
using AlvMod.Core.Modulacao;

namespace AlvMod.Core.Planta
{
    /// <summary>Peças das duas fiadas do pavimento, trecho a trecho.</summary>
    public sealed class ModulacaoFiadas
    {
        public List<(TrechoFiada trecho, ResultadoModulacao resultado)> Fiada1 { get; } =
            new List<(TrechoFiada trecho, ResultadoModulacao resultado)>();

        public List<(TrechoFiada trecho, ResultadoModulacao resultado)> Fiada2 { get; } =
            new List<(TrechoFiada trecho, ResultadoModulacao resultado)>();

        public List<(TrechoFiada trecho, ResultadoModulacao resultado)> Fiada(int fiada) => fiada % 2 == 1 ? Fiada1 : Fiada2;
    }

    /// <summary>
    /// Modula as duas fiadas do pavimento. A 1ª fiada segue a regra do engenheiro (os 34 juntos na ponta de
    /// ajuste); só quando isso deixaria a 2ª fiada mal amarrada (junta a menos de 15 cm da de baixo, ou regra
    /// relaxada) os 34 da 1ª fiada são redistribuídos entre as duas pontas do trecho.
    /// </summary>
    public static class ModulacaoPavimento
    {
        public static ModulacaoFiadas Modular(SolucaoPavimento solucao, Modulador modulador, OpcoesFiadas op)
        {
            var r = new ModulacaoFiadas();
            var f1 = solucao.Fiada1.Trechos.Select(t => (trecho: t, resultado: modulador.PrimeiraFiada(t.Comprimento, t.Pontas))).ToList();
            for (int i = 0; i < f1.Count; i++)
                f1[i] = (f1[i].trecho, PassaNasDuasPontas(f1[i].trecho, f1[i].resultado, modulador, CentroAoLongo(solucao, f1[i].trecho.Parede)));

            foreach (var parede in f1.Select(x => x.trecho.Parede).Distinct().ToList())
            {
                var meus1 = Enumerable.Range(0, f1.Count).Where(i => f1[i].trecho.Parede == parede).ToList();
                var meus2 = solucao.Fiada2.Trechos.Where(t => t.Parede == parede).ToList();
                if (meus2.Count == 0) continue;
                for (int volta = 0; volta < 2; volta++)
                {
                    foreach (int i in meus1)
                    {
                        var atual = f1[i];
                        double melhor = Defasagem(f1, parede, meus2, solucao.Fiada1.Fixas, modulador, op);
                        foreach (var alt in Alternativas(atual.trecho, atual.resultado, modulador))
                        {
                            f1[i] = (atual.trecho, alt);
                            double d = Defasagem(f1, parede, meus2, solucao.Fiada1.Fixas, modulador, op);
                            if (d < melhor - 1e-9) { melhor = d; atual = f1[i]; }
                        }
                        f1[i] = atual;
                    }
                }
            }

            r.Fiada1.AddRange(f1);
            foreach (var t in solucao.Fiada2.Trechos)
            {
                var arestas = PlanejadorFiadas.Arestas(f1, t.Parede, op.Junta, solucao.Fiada1.Fixas).Select(a => a - t.Inicio).ToList();
                r.Fiada2.Add((t, modulador.SegundaFiada(t.Comprimento, arestas, t.Pontas)));
            }
            return r;
        }

        /// <summary>
        /// Trecho da 1ª fiada que passa nas duas pontas (vai de face de fora a face de fora): cada ponta leva um
        /// 34 e os outros 34 ficam juntos na ponta mais longe do centro do pavimento (como o engenheiro faz).
        /// </summary>
        private static ResultadoModulacao PassaNasDuasPontas(TrechoFiada t, ResultadoModulacao r, Modulador modulador, double centro)
        {
            if (t.TipoInicio != TipoPonta.Passa || t.TipoFim != TipoPonta.Passa || r == null || !r.Sucesso) return r;
            var seq = r.Pecas.Select(p => p.Peca).ToList();
            if (!seq.All(p => p.Funcao == FuncaoPeca.Principal || p.Funcao == FuncaoPeca.Amarracao)) return r;
            var p34 = seq.FirstOrDefault(p => p.Funcao == FuncaoPeca.Amarracao);
            var p39 = seq.FirstOrDefault(p => p.Funcao == FuncaoPeca.Principal);
            int n34 = seq.Count(p => p == p34);
            if (p34 == null || p39 == null || n34 < 2 || seq.Count(p => p == p39) + n34 != seq.Count) return r;
            bool extrasNoInicio = Math.Abs(t.Inicio - centro) > Math.Abs(t.Fim - centro) + 5;
            var nova = new List<Peca>();
            nova.AddRange(Enumerable.Repeat(p34, extrasNoInicio ? n34 - 1 : 1));
            nova.AddRange(Enumerable.Repeat(p39, seq.Count - n34));
            nova.AddRange(Enumerable.Repeat(p34, extrasNoInicio ? 1 : n34 - 1));
            return modulador.Reordenar(r, nova, t.Pontas) ?? r;
        }

        private static double CentroAoLongo(SolucaoPavimento s, Parede w)
        {
            var coords = s.Fiada1.Trechos.Select(t => t.Parede).Distinct().Where(p => p.Orientacao != w.Orientacao).Select(p => p.Coord).ToList();
            return coords.Count == 0 ? (w.Inicio + w.Fim) / 2 : (coords.Min() + coords.Max()) / 2;
        }

        /// <summary>
        /// Quanto as juntas da 2ª fiada desta parede ficam longe de meio bloco das juntas da 1ª (cm somados),
        /// com peso alto para trecho que precisou relaxar regra ou não fechou.
        /// </summary>
        private static double Defasagem(List<(TrechoFiada trecho, ResultadoModulacao resultado)> f1, Parede parede,
            List<TrechoFiada> trechos2, List<PecaFixa> fixas1, Modulador modulador, OpcoesFiadas op)
        {
            var juntas1 = PlanejadorFiadas.Arestas(f1, parede, op.Junta, fixas1);
            double total = 0;
            foreach (var t in trechos2)
            {
                var r = modulador.SegundaFiada(t.Comprimento, juntas1.Select(a => a - t.Inicio), t.Pontas);
                if (!r.Sucesso) { total += 10000; continue; }
                total += 1000 * r.Avisos.Count;
                foreach (var j in r.Juntas())
                {
                    double d = juntas1.Count == 0 ? 20 : juntas1.Min(x => Math.Abs(x - (j + t.Inicio)));
                    total += Math.Max(0, 15 - d);
                }
            }
            return total;
        }

        /// <summary>
        /// As mesmas peças do trecho com os 34 divididos de outro jeito entre as duas pontas (19 e bolacha
        /// nas pontas não mudam de lugar).
        /// </summary>
        private static IEnumerable<ResultadoModulacao> Alternativas(TrechoFiada t, ResultadoModulacao r, Modulador modulador)
        {
            if (r == null || !r.Sucesso || r.Pecas.Count < 2) yield break;
            var seq = r.Pecas.Select(p => p.Peca).ToList();
            bool Livre(Peca p) => p.Funcao == FuncaoPeca.Principal || p.Funcao == FuncaoPeca.Amarracao;
            int a = Livre(seq[0]) ? 0 : 1;
            int b = Livre(seq[seq.Count - 1]) ? seq.Count : seq.Count - 1;
            if (b <= a) yield break;
            var meio = seq.GetRange(a, b - a);
            if (!meio.All(Livre)) yield break;
            var p34 = meio.FirstOrDefault(p => p.Funcao == FuncaoPeca.Amarracao);
            var p39 = meio.FirstOrDefault(p => p.Funcao == FuncaoPeca.Principal);
            if (p34 == null || p39 == null) yield break;
            int n34 = meio.Count(p => p == p34), n39 = meio.Count - n34;
            if (n34 + n39 != meio.Count) yield break; // mais de um tipo de 34/39 (catálogo editado): não mexe

            for (int k = 0; k <= n34; k++)
            {
                var nova = new List<Peca>();
                nova.AddRange(seq.Take(a));
                nova.AddRange(Enumerable.Repeat(p34, k));
                nova.AddRange(Enumerable.Repeat(p39, n39));
                nova.AddRange(Enumerable.Repeat(p34, n34 - k));
                nova.AddRange(seq.Skip(b));
                if (nova.SequenceEqual(seq)) continue;
                var alt = modulador.Reordenar(r, nova, t.Pontas);
                if (alt != null) yield return alt;
            }
        }
    }
}
