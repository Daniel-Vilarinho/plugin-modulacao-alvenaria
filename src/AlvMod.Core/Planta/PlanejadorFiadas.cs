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

        /// <summary>Pontas que encostam em vão ou encontro: nelas não entra compensador.</summary>
        public Bordas Bordas { get; set; }

        /// <summary>Pontas que chegam num encontro de paredes: nelas pode entrar o bloco de encontro (54).</summary>
        public Bordas Encontros { get; set; }

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

        /// <summary>Folga de marco somada ao vão de porta (cm, total).</summary>
        public double FolgaPorta { get; set; } = 11;

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
        public static List<TrechoFiada> Trechos(PlantaDetectada planta, int fiada, OpcoesFiadas op, List<string> avisos = null)
        {
            var resultado = new List<TrechoFiada>();
            double meia = op.Largura / 2, recuo = op.Largura / 2 + op.Junta;

            foreach (var w in planta.Paredes)
            {
                double inicio = w.Inicio, fim = w.Fim;
                bool encontroNoInicio = false, encontroNoFim = false;
                var cortes = new List<(double a, double b, bool vao)>();

                foreach (var e in planta.EncontrosDe(w))
                {
                    if (e.Tipo == TipoEncontro.Outro) continue;
                    double n = w.AoLongo(e.X, e.Y);
                    bool passa = e.Dominante(fiada) == w;
                    if (e.Terminais.Contains(w))
                    {
                        bool noInicio = Math.Abs(n - w.Inicio) <= Math.Abs(n - w.Fim);
                        double novo = passa ? (noInicio ? n - meia : n + meia) : (noInicio ? n + recuo : n - recuo);
                        if (noInicio) { inicio = novo; encontroNoInicio = true; }
                        else { fim = novo; encontroNoFim = true; }
                    }
                    else if (!passa)
                    {
                        cortes.Add((n - recuo, n + recuo, false));
                    }
                }

                foreach (var v in w.VaosQueCortam())
                {
                    double folga = v.Tipo == TipoVao.Porta ? op.FolgaPorta / 2 : 0;
                    cortes.Add((v.Inicio - folga, v.Fim + folga, true));
                }

                // Intervalos que sobram, com o motivo de cada ponta (vão / encontro / livre).
                var intervalos = new List<(double a, double b, bool vaoA, bool encA, bool vaoB, bool encB)>();
                double cursor = inicio;
                bool cursorVao = false, cursorEnc = encontroNoInicio;
                foreach (var (a, b, vao) in cortes.OrderBy(c => c.a))
                {
                    if (b <= cursor) continue;
                    if (a > cursor)
                        intervalos.Add((cursor, Math.Min(a, fim), cursorVao, cursorEnc, vao, !vao));
                    cursor = Math.Max(cursor, b);
                    cursorVao = vao;
                    cursorEnc = !vao;
                    if (cursor >= fim) break;
                }
                if (cursor < fim)
                    intervalos.Add((cursor, fim, cursorVao, cursorEnc, false, encontroNoFim));

                // Sufixo -A, -B… quando portas/passagens dividem a parede.
                var divisoes = w.VaosQueCortam().Select(v => (v.Inicio + v.Fim) / 2).ToList();
                foreach (var iv in intervalos)
                {
                    double comp = iv.b - iv.a;
                    if (comp < op.TrechoMinimo)
                    {
                        if (comp > 0.5) avisos?.Add($"{w.Nome}: trecho de {comp:0.#} cm entre {iv.a:0.#} e {iv.b:0.#} é curto demais e foi ignorado.");
                        continue;
                    }
                    var bordas = Bordas.Nenhuma;
                    if (iv.vaoA || (op.SemCompensadorNosEncontros && iv.encA)) bordas |= Bordas.Inicio;
                    if (iv.vaoB || (op.SemCompensadorNosEncontros && iv.encB)) bordas |= Bordas.Fim;

                    string nome = w.Nome ?? $"#{w.Indice}";
                    if (divisoes.Count > 0)
                    {
                        int segmento = divisoes.Count(d => d < (iv.a + iv.b) / 2);
                        nome += "-" + (char)('A' + Math.Min(segmento, 25));
                    }

                    var encontros = Bordas.Nenhuma;
                    if (iv.encA) encontros |= Bordas.Inicio;
                    if (iv.encB) encontros |= Bordas.Fim;

                    resultado.Add(new TrechoFiada
                    {
                        Parede = w,
                        Fiada = fiada,
                        Inicio = iv.a,
                        Fim = iv.b,
                        Bordas = bordas,
                        Encontros = encontros,
                        Nome = nome
                    });
                }
            }
            return resultado;
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
