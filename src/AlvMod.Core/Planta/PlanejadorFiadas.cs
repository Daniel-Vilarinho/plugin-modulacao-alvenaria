using System;
using System.Collections.Generic;
using System.Linq;
using AlvMod.Core.Catalogo;
using AlvMod.Core.Modulacao;

namespace AlvMod.Core.Planta
{
    /// <summary>Trecho contínuo de alvenaria de uma parede numa fiada (cm, coordenadas ao longo do eixo).</summary>
    public sealed class TrechoFiada
    {
        public Parede Parede { get; set; }
        public int Fiada { get; set; }
        public double Inicio { get; set; }
        public double Fim { get; set; }

        /// <summary>O que existe em cada ponta (porta, encontro, canto): define onde cada peça pode entrar.</summary>
        public Pontas Pontas { get; set; } = new Pontas();

        /// <summary>Nome da parede com sufixo do segmento entre portas (ex.: "PAREDE 11-B").</summary>
        public string Nome { get; set; }

        public double Comprimento => Fim - Inicio;

        public override string ToString() => $"{Nome} [{Inicio:0.#}; {Fim:0.#}] = {Comprimento:0.#} cm";
    }

    /// <summary>Peça com posição fixa (o 54 centrado nos encontros em T e em cruz).</summary>
    public sealed class PecaFixa
    {
        public Parede Parede { get; set; }
        public Peca Peca { get; set; }
        public double Inicio { get; set; }
        public double Fim => Inicio + Peca.Comprimento;
    }

    /// <summary>Uma fiada planejada: trechos a modular e peças fixas.</summary>
    public sealed class PlanoFiada
    {
        public List<TrechoFiada> Trechos { get; } = new List<TrechoFiada>();
        public List<PecaFixa> Fixas { get; } = new List<PecaFixa>();
    }

    public sealed class OpcoesFiadas
    {
        /// <summary>Largura do bloco (cm).</summary>
        public double Largura { get; set; } = 14;

        public double Junta { get; set; } = 1;

        /// <summary>Folga de marco somada ao tamanho da porta (cm, total): vão + 11.</summary>
        public double FolgaPorta { get; set; } = 11;

        /// <summary>Folga de marco somada ao tamanho da janela (cm, total): vão + 6.</summary>
        public double FolgaJanela { get; set; } = 6;

        /// <summary>Compensador também fica proibido nas pontas que chegam em encontro (prumada de graute).</summary>
        public bool SemCompensadorNosEncontros { get; set; } = true;

        /// <summary>Trechos mais curtos que isso são descartados com aviso (cm).</summary>
        public double TrechoMinimo { get; set; } = 4;

        /// <summary>
        /// Bloco de encontro (54). Quando informado, os encontros em T e em cruz recebem um 54 fixo,
        /// centrado no encontro, na parede que passa direto naquela fiada. Sem ele, a parede passa contínua.
        /// </summary>
        public Peca BlocoEncontro { get; set; }
    }

    /// <summary>
    /// Transforma as paredes detectadas em trechos de alvenaria de cada fiada, com a amarração direta da
    /// família 39 (meio bloco de defasagem entre as fiadas):
    ///  - em cada encontro passa direto UMA parede, alternando entre as fiadas (ímpar: a contínua/maior;
    ///    par: a perpendicular a ela);
    ///  - canto (L) e parede que chega num T: a que passa avança até a face externa da outra e termina em 34;
    ///    a que não passa recua até a face da outra, com 1 cm de junta, e começa com 39;
    ///  - T e cruz: a parede contínua que passa recebe um 54 fixo centrado no encontro; quando não passa,
    ///    é cortada em volta da outra (1 cm de junta de cada lado) e as peças junto ao corte são 39;
    ///  - portas e passagens cortam a alvenaria (porta = tamanho + folga de marco); janelas não cortam
    ///    a 1ª e a 2ª fiada.
    /// </summary>
    public static class PlanejadorFiadas
    {
        /// <summary>O que há numa ponta de trecho.</summary>
        private sealed class Ponta
        {
            public bool Porta;      // porta ou passagem
            public bool Encontro;   // encosta em outra parede (a parede continua ali)
            public bool Passa34;    // passa direto no canto / atravessa o T
            public bool Exige39;    // encosta na parede que passa ou no 54
        }

        private sealed class Corte
        {
            public double A, B;
            public Ponta Depois;    // ponta do trecho que termina em A
            public Ponta Antes;     // ponta do trecho que começa em B
        }

        public static PlanoFiada Planejar(PlantaDetectada planta, int fiada, OpcoesFiadas op, List<string> avisos = null)
        {
            var plano = new PlanoFiada();
            double meia = op.Largura / 2, recuo = op.Largura / 2 + op.Junta;

            foreach (var w in planta.Paredes)
            {
                double inicio = w.Inicio, fim = w.Fim;
                Ponta pontaInicio = null, pontaFim = null;
                var cortes = new List<Corte>();
                var centros54 = new List<double>();

                foreach (var e in planta.EncontrosDe(w))
                {
                    double n = w.AoLongo(e.X, e.Y);
                    if (e.Tipo == TipoEncontro.Outro)
                    {
                        // Encontro que o detector não reconheceu: a parede continua ali (sem 19 nem bolacha
                        // na ponta), mas a amarração fica para o projetista.
                        if (!e.Terminais.Contains(w)) continue;
                        var irregular = new Ponta { Encontro = true };
                        if (Math.Abs(n - w.Inicio) <= Math.Abs(n - w.Fim)) pontaInicio = irregular; else pontaFim = irregular;
                        avisos?.Add($"{w.Nome}: encontro não reconhecido em ({e.X:0.#}; {e.Y:0.#}); conferir a amarração.");
                        continue;
                    }
                    bool passa = e.Dominante(fiada) == w;
                    if (e.Terminais.Contains(w))
                    {
                        bool noInicio = Math.Abs(n - w.Inicio) <= Math.Abs(n - w.Fim);
                        double novo = passa ? (noInicio ? n - meia : n + meia) : (noInicio ? n + recuo : n - recuo);
                        var ponta = new Ponta { Encontro = true, Passa34 = passa, Exige39 = !passa };
                        if (noInicio) { inicio = novo; pontaInicio = ponta; }
                        else { fim = novo; pontaFim = ponta; }
                    }
                    else if (passa)
                    {
                        if (op.BlocoEncontro != null && (e.Tipo == TipoEncontro.T || e.Tipo == TipoEncontro.X))
                            centros54.Add(n);
                    }
                    else
                    {
                        cortes.Add(new Corte
                        {
                            A = n - recuo,
                            B = n + recuo,
                            Depois = new Ponta { Encontro = true, Exige39 = true },
                            Antes = new Ponta { Encontro = true, Exige39 = true }
                        });
                    }
                }

                // 54 fixo centrado no T/cruz. Encontros muito próximos (ex.: parede dupla) dividem um 54 só.
                foreach (var grupo in AgruparEncontros(centros54, op))
                {
                    double l54 = op.BlocoEncontro.Comprimento;
                    double min = grupo.Min(), max = grupo.Max(), c = (min + max) / 2;
                    double a = c - l54 / 2, b = c + l54 / 2;
                    bool cobre = (max - min) + op.Largura + 2 * op.Junta <= l54;
                    if (!cobre || a < inicio - 0.01 || b > fim + 0.01)
                    {
                        avisos?.Add($"{w.Nome}: o 54 do encontro em {c:0.#} não coube (encontros muito próximos ou perto da ponta); parede contínua ali.");
                        continue;
                    }
                    plano.Fixas.Add(new PecaFixa { Parede = w, Peca = op.BlocoEncontro, Inicio = a });
                    cortes.Add(new Corte
                    {
                        A = a - op.Junta,
                        B = b + op.Junta,
                        Depois = new Ponta { Encontro = true, Exige39 = true },
                        Antes = new Ponta { Encontro = true, Exige39 = true }
                    });
                }

                // Portas e passagens cortam a alvenaria: abertura = tamanho da porta lido do desenho + folga de marco.
                foreach (var v in w.VaosQueCortam())
                {
                    var (a, b) = v.Alvenaria(v.Tipo == TipoVao.Porta ? op.FolgaPorta : 0);
                    cortes.Add(new Corte { A = a, B = b, Depois = new Ponta { Porta = true }, Antes = new Ponta { Porta = true } });
                }

                // Intervalos que sobram, com o que há em cada ponta.
                var intervalos = new List<(double a, double b, Ponta pa, Ponta pb)>();
                double cursor = inicio;
                var cursorPonta = pontaInicio;
                foreach (var c in cortes.OrderBy(c => c.A))
                {
                    if (c.B <= cursor) continue;
                    if (c.A > cursor)
                        intervalos.Add((cursor, Math.Min(c.A, fim), cursorPonta, c.Depois));
                    cursor = Math.Max(cursor, c.B);
                    cursorPonta = c.Antes;
                    if (cursor >= fim) break;
                }
                if (cursor < fim)
                    intervalos.Add((cursor, fim, cursorPonta, pontaFim));

                // Sufixo -A, -B… quando portas/passagens dividem a parede.
                var divisoes = w.VaosQueCortam().Select(v => v.Centro).ToList();
                foreach (var iv in intervalos)
                {
                    double comp = iv.b - iv.a;
                    if (comp < op.TrechoMinimo)
                    {
                        if (comp > 0.5) avisos?.Add($"{w.Nome}: trecho de {comp:0.#} cm entre {iv.a:0.#} e {iv.b:0.#} é curto demais e foi ignorado.");
                        continue;
                    }

                    plano.Trechos.Add(new TrechoFiada
                    {
                        Parede = w,
                        Fiada = fiada,
                        Inicio = iv.a,
                        Fim = iv.b,
                        Pontas = ParaPontas(iv.pa, iv.pb, op),
                        Nome = NomeDoSegmento(w, divisoes, (iv.a + iv.b) / 2)
                    });
                }
            }
            return plano;
        }

        private static string NomeDoSegmento(Parede w, List<double> divisoes, double posicao)
        {
            string nome = w.Nome ?? $"#{w.Indice}";
            if (divisoes.Count == 0) return nome;
            int segmento = divisoes.Count(d => d < posicao);
            return nome + "-" + (char)('A' + Math.Min(segmento, 25));
        }

        /// <summary>Encontros cujos 54 se sobreporiam viram um grupo só.</summary>
        private static IEnumerable<List<double>> AgruparEncontros(List<double> centros, OpcoesFiadas op)
        {
            if (op.BlocoEncontro == null || centros.Count == 0) yield break;
            double alcance = op.BlocoEncontro.Comprimento + op.Junta;
            var grupo = new List<double>();
            foreach (var c in centros.OrderBy(c => c))
            {
                if (grupo.Count > 0 && c - grupo[grupo.Count - 1] >= alcance)
                {
                    yield return grupo;
                    grupo = new List<double>();
                }
                grupo.Add(c);
            }
            if (grupo.Count > 0) yield return grupo;
        }

        private static Pontas ParaPontas(Ponta a, Ponta b, OpcoesFiadas op)
        {
            var p = new Pontas();
            void Marca(Ponta x, Bordas lado)
            {
                if (x == null) return;
                if (x.Porta || (op.SemCompensadorNosEncontros && x.Encontro)) p.SemCompensador |= lado;
                if (x.Porta) p.Porta |= lado;
                if (x.Encontro) p.Encontro |= lado;
                if (x.Passa34) p.Passa34 |= lado;
                if (x.Exige39) p.Exige39 |= lado;
            }
            Marca(a, Bordas.Inicio);
            Marca(b, Bordas.Fim);
            return p;
        }

        /// <summary>
        /// Posições (cm, ao longo do eixo) da cota corrente da parede: começo e fim da alvenaria, abertura das
        /// portas (tamanho + 11) e das janelas (tamanho + 6). Os cortes em volta dos encontros não entram.
        /// </summary>
        public static List<double> PontosDeCota(Parede w, IEnumerable<TrechoFiada> trechos, OpcoesFiadas op,
            IEnumerable<PecaFixa> fixas = null)
        {
            var extremos = trechos.Where(t => t.Parede == w).SelectMany(t => new[] { t.Inicio, t.Fim })
                .Concat((fixas ?? Enumerable.Empty<PecaFixa>()).Where(f => f.Parede == w).SelectMany(f => new[] { f.Inicio, f.Fim }))
                .ToList();
            if (extremos.Count == 0) return new List<double>();

            double ini = extremos.Min(), fim = extremos.Max();
            var pts = new List<double> { ini, fim };
            foreach (var v in w.Vaos)
            {
                double folga = v.Tipo == TipoVao.Porta ? op.FolgaPorta : v.Tipo == TipoVao.Janela ? op.FolgaJanela : 0;
                var (a, b) = v.Alvenaria(folga);
                if (a > ini && b < fim) { pts.Add(a); pts.Add(b); }
            }
            var unicos = new List<double>();
            foreach (var x in pts.OrderBy(x => x))
                if (unicos.Count == 0 || x - unicos[unicos.Count - 1] > 0.05) unicos.Add(x);
            return unicos;
        }

        /// <summary>
        /// Arestas (cm, ao longo do eixo) de todas as peças de uma parede numa fiada já modulada, incluindo as
        /// peças fixas: são as posições que a fiada seguinte deve evitar (junta a prumo).
        /// </summary>
        public static List<double> Arestas(IEnumerable<(TrechoFiada trecho, ResultadoModulacao resultado)> fiada, Parede parede,
            double junta = 1, IEnumerable<PecaFixa> fixas = null)
        {
            var arestas = new List<double>();
            foreach (var (t, r) in fiada.Where(x => x.trecho.Parede == parede))
                foreach (var p in r.Pecas)
                {
                    arestas.Add(t.Inicio + p.Inicio);
                    arestas.Add(t.Inicio + p.Fim);
                }
            foreach (var f in (fixas ?? Enumerable.Empty<PecaFixa>()).Where(f => f.Parede == parede))
            {
                arestas.Add(f.Inicio);
                arestas.Add(f.Fim);
            }
            return CentrosDeJunta(arestas, junta);
        }

        /// <summary>
        /// Une arestas a até uma junta de distância (fim de uma peça e início da seguinte) no centro da
        /// junta; arestas soltas (ponta de trecho) ficam como estão.
        /// </summary>
        public static List<double> CentrosDeJunta(IEnumerable<double> arestas, double junta = 1)
        {
            var ordenadas = arestas.OrderBy(a => a).ToList();
            var centros = new List<double>();
            for (int i = 0; i < ordenadas.Count; i++)
            {
                if (i + 1 < ordenadas.Count && ordenadas[i + 1] - ordenadas[i] <= junta + 0.6)
                {
                    centros.Add((ordenadas[i] + ordenadas[i + 1]) / 2);
                    i++;
                }
                else
                {
                    centros.Add(ordenadas[i]);
                }
            }
            return centros;
        }
    }

    /// <summary>
    /// Numeração das paredes (questionário, item 48): primeiro as do eixo X (horizontais), de cima
    /// para baixo; depois as do eixo Y (verticais), da esquerda para a direita.
    /// </summary>
    public static class Numeracao
    {
        public static void Numerar(PlantaDetectada planta, string prefixo = "PAREDE ")
        {
            int n = 1;
            foreach (var p in planta.Paredes.Where(p => p.Horizontal)
                         .OrderByDescending(p => Math.Round(p.Coord, 0)).ThenBy(p => p.Inicio))
                p.Nome = prefixo + n++;
            foreach (var p in planta.Paredes.Where(p => !p.Horizontal)
                         .OrderBy(p => Math.Round(p.Coord, 0)).ThenByDescending(p => p.Fim))
                p.Nome = prefixo + n++;
        }
    }
}
