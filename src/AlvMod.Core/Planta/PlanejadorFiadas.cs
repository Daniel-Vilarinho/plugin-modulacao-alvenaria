using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>O que existe em cada ponta (porta, encontro em T, canto): define onde cada peça pode entrar.</summary>
        public Pontas Pontas { get; set; } = new Pontas();

        /// <summary>Nome da parede com sufixo do segmento entre portas (ex.: "PAREDE 11-B").</summary>
        public string Nome { get; set; }

        public double Comprimento => Fim - Inicio;

        public override string ToString() => $"{Nome} [{Inicio:0.#}; {Fim:0.#}] = {Comprimento:0.#} cm";
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
    }

    /// <summary>
    /// Transforma as paredes detectadas em trechos de alvenaria de cada fiada, aplicando a amarração
    /// direta nos encontros (questionário, itens 16 e 17):
    ///  - em cada encontro passa direto UMA parede, alternando entre fiadas (ímpar: a passante/maior;
    ///    par: a perpendicular a ela);
    ///  - a parede que passa e termina no encontro avança até a face externa da outra (eixo + largura/2);
    ///  - a que não passa e termina ali recua até a face da que passa, com junta (eixo − largura/2 − junta);
    ///  - a que não passa mas continua do outro lado é cortada em volta do encontro;
    ///  - portas e passagens cortam a alvenaria (porta = vão + folga de marco); janelas não cortam
    ///    a 1ª e a 2ª fiada.
    /// </summary>
    public static class PlanejadorFiadas
    {
        /// <summary>O que há numa ponta de trecho.</summary>
        private sealed class Ponta
        {
            public bool Porta;      // porta ou passagem
            public bool Encontro;   // encosta em outra parede
            public bool T54;        // parede que atravessa um T: lugar do 54
            public bool Canto;      // canto (L)
        }

        private sealed class Corte
        {
            public double A, B;
            public Ponta Depois;    // ponta do trecho que termina em A
            public Ponta Antes;     // ponta do trecho que começa em B
        }

        public static List<TrechoFiada> Trechos(PlantaDetectada planta, int fiada, OpcoesFiadas op, List<string> avisos = null)
        {
            var resultado = new List<TrechoFiada>();
            double meia = op.Largura / 2, recuo = op.Largura / 2 + op.Junta;

            foreach (var w in planta.Paredes)
            {
                double inicio = w.Inicio, fim = w.Fim;
                Ponta pontaInicio = null, pontaFim = null;
                var cortes = new List<Corte>();

                foreach (var e in planta.EncontrosDe(w))
                {
                    if (e.Tipo == TipoEncontro.Outro) continue;
                    double n = w.AoLongo(e.X, e.Y);
                    bool passa = e.Dominante(fiada) == w;
                    if (e.Terminais.Contains(w))
                    {
                        bool noInicio = Math.Abs(n - w.Inicio) <= Math.Abs(n - w.Fim);
                        double novo = passa ? (noInicio ? n - meia : n + meia) : (noInicio ? n + recuo : n - recuo);
                        var ponta = new Ponta
                        {
                            Encontro = true,
                            Canto = e.Tipo == TipoEncontro.L,
                            // O 54 só no T, na ponta da parede que atravessa a outra (a que passa direto nesta fiada).
                            T54 = passa && e.Tipo == TipoEncontro.T && e.Passantes.Count > 0
                        };
                        if (noInicio) { inicio = novo; pontaInicio = ponta; }
                        else { fim = novo; pontaFim = ponta; }
                    }
                    else if (!passa)
                    {
                        cortes.Add(new Corte
                        {
                            A = n - recuo,
                            B = n + recuo,
                            Depois = new Ponta { Encontro = true },
                            Antes = new Ponta { Encontro = true }
                        });
                    }
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

                    string nome = w.Nome ?? $"#{w.Indice}";
                    if (divisoes.Count > 0)
                    {
                        int segmento = divisoes.Count(d => d < (iv.a + iv.b) / 2);
                        nome += "-" + (char)('A' + Math.Min(segmento, 25));
                    }

                    resultado.Add(new TrechoFiada
                    {
                        Parede = w,
                        Fiada = fiada,
                        Inicio = iv.a,
                        Fim = iv.b,
                        Pontas = ParaPontas(iv.pa, iv.pb, op),
                        Nome = nome
                    });
                }
            }
            return resultado;
        }

        private static Pontas ParaPontas(Ponta a, Ponta b, OpcoesFiadas op)
        {
            var p = new Pontas();
            void Marca(Ponta x, Bordas lado)
            {
                if (x == null) return;
                if (x.Porta || (op.SemCompensadorNosEncontros && x.Encontro)) p.SemCompensador |= lado;
                if (x.Porta) p.Porta |= lado;
                if (x.T54) p.EncontroT |= lado;
                if (x.Canto) p.Canto |= lado;
            }
            Marca(a, Bordas.Inicio);
            Marca(b, Bordas.Fim);
            return p;
        }

        /// <summary>
        /// Posições (cm, ao longo do eixo) onde a cota corrente da parede quebra: pontas dos trechos (inclui a
        /// abertura das portas) e a abertura das janelas (tamanho + 6), que não cortam a 1ª e a 2ª fiada.
        /// </summary>
        public static List<double> PontosDeCota(Parede w, IEnumerable<TrechoFiada> trechos, OpcoesFiadas op)
        {
            var pts = new List<double>();
            foreach (var t in trechos.Where(t => t.Parede == w)) { pts.Add(t.Inicio); pts.Add(t.Fim); }
            foreach (var v in w.Vaos.Where(v => v.Tipo == TipoVao.Janela))
            {
                var (a, b) = v.Alvenaria(op.FolgaJanela);
                if (a > w.Inicio && b < w.Fim) { pts.Add(a); pts.Add(b); }
            }
            var unicos = new List<double>();
            foreach (var x in pts.OrderBy(x => x))
                if (unicos.Count == 0 || x - unicos[unicos.Count - 1] > 0.05) unicos.Add(x);
            return unicos;
        }

        /// <summary>
        /// Arestas (cm, ao longo do eixo) de todas as peças de uma parede numa fiada já modulada: são as
        /// posições que a fiada seguinte deve evitar (junta a prumo).
        /// </summary>
        public static List<double> Arestas(IEnumerable<(TrechoFiada trecho, ResultadoModulacao resultado)> fiada, Parede parede,
            double junta = 1)
        {
            var arestas = new List<double>();
            foreach (var (t, r) in fiada.Where(x => x.trecho.Parede == parede))
                foreach (var p in r.Pecas)
                {
                    arestas.Add(t.Inicio + p.Inicio);
                    arestas.Add(t.Inicio + p.Fim);
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
                if (i + 1 < ordenadas.Count && ordenadas[i + 1] - ordenadas[i] <= junta + 0.5)
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
