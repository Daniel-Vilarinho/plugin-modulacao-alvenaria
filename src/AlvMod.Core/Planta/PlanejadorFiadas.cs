using System;
using System.Collections.Generic;
using System.Linq;
using AlvMod.Core.Catalogo;
using AlvMod.Core.Modulacao;

namespace AlvMod.Core.Planta
{
    /// <summary>O que há numa ponta de trecho de alvenaria.</summary>
    public enum TipoPonta
    {
        /// <summary>Ponta de parede solta (sem continuidade).</summary>
        Solta,
        /// <summary>Ombreira de porta ou passagem.</summary>
        Porta,
        /// <summary>A parede passa direto no encontro, até a face de fora da outra.</summary>
        Passa,
        /// <summary>A parede encosta no 54 ou na parede contínua de um T/cruz, com 1 cm de junta.</summary>
        Encosta,
        /// <summary>A parede encosta na outra num canto (L), com 1 cm de junta.</summary>
        EncostaCanto,
        /// <summary>A parede é cortada pela que chega no T e atravessa até a face de fora (1 cm de junta de cada lado).</summary>
        Corte,
        /// <summary>A parede é cortada na cruz pela outra, que segue contínua (1 cm de junta de cada lado).</summary>
        Cruza
    }

    /// <summary>Trecho contínuo de alvenaria de uma parede numa fiada (cm, coordenadas ao longo do eixo).</summary>
    public sealed class TrechoFiada
    {
        public Parede Parede { get; set; }
        public int Fiada { get; set; }
        public double Inicio { get; set; }
        public double Fim { get; set; }
        public TipoPonta TipoInicio { get; set; }
        public TipoPonta TipoFim { get; set; }

        /// <summary>O que existe em cada ponta, no formato do motor de modulação.</summary>
        public Pontas Pontas { get; set; } = new Pontas();

        /// <summary>Nome da parede (ex.: "PAREDE 6").</summary>
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

        /// <summary>Folga de marco somada à medida nominal da porta (cm, total): 80 → 91.</summary>
        public double FolgaPorta { get; set; } = 11;

        /// <summary>Folga somada à medida nominal da janela (cm, total): 150 → 151. Só aparece nas cotas.</summary>
        public double FolgaJanela { get; set; } = 1;

        /// <summary>Trechos mais curtos que isso são descartados com aviso (cm). A menor peça (bolacha 4) cabe em 3,5.</summary>
        public double TrechoMinimo { get; set; } = 3.5;

        /// <summary>
        /// Bloco de encontro (54). Quando informado, os encontros em T e em cruz recebem um 54 fixo,
        /// centrado no encontro, na parede que passa direto naquela fiada. Sem ele, a parede passa contínua.
        /// </summary>
        public Peca BlocoEncontro { get; set; }

        /// <summary>
        /// Quanto (cm) o planejador pode deslocar uma porta para os pedaços de parede dos dois lados fecharem
        /// com blocos inteiros. 0 = portas exatamente onde a arquitetura desenhou.
        /// </summary>
        public double DeslocamentoMaximoPorta { get; set; } = 12;
    }

    /// <summary>
    /// Qual direção fica "por cima" num encontro: na 1ª fiada passa a parede dessa direção (no L ela vai
    /// até a face de fora da outra; no T e na cruz ela segue contínua, com o 54 centrado); na 2ª fiada inverte.
    /// </summary>
    public sealed class EstadoEncontro
    {
        public EstadoEncontro(Encontro encontro) => Encontro = encontro;

        public Encontro Encontro { get; }

        /// <summary>Na 1ª fiada passa a parede vertical.</summary>
        public bool VerticalPorCima { get; set; }

        /// <summary>Estado imposto (lido da 1ª fiada desenhada): o planejador não muda.</summary>
        public bool Fixo { get; set; }

        public bool VerticalPorCimaNa(int fiada) => fiada % 2 == 1 ? VerticalPorCima : !VerticalPorCima;
    }

    /// <summary>Resultado do planejamento do pavimento: as duas fiadas, os encontros e os pontos de graute.</summary>
    public sealed class SolucaoPavimento
    {
        public List<EstadoEncontro> Estados { get; } = new List<EstadoEncontro>();
        public PlanoFiada Fiada1 { get; set; }
        public PlanoFiada Fiada2 { get; set; }

        /// <summary>Ponto de graute (cm) em cada encontro de alvenaria: a mesma posição nas duas fiadas.</summary>
        public List<(double X, double Y)> Grautes { get; } = new List<(double X, double Y)>();

        public List<string> Avisos { get; } = new List<string>();

        /// <summary>Custo total da solução na escala interna do planejador (para comparar soluções).</summary>
        public long Custo { get; set; }

        public PlanoFiada Plano(int fiada) => fiada % 2 == 1 ? Fiada1 : Fiada2;
    }

    /// <summary>
    /// Transforma as paredes detectadas em trechos de alvenaria de cada fiada, do jeito que o engenheiro modula
    /// (conferido numa modulação à mão do engenheiro):
    ///  - em cada encontro uma direção fica por cima na 1ª fiada e a outra na 2ª (amarração direta);
    ///  - L: a que passa vai até a face de fora da outra (termina em 34 ou 39); a outra encosta com 1 cm;
    ///  - T e cruz: por cima, a contínua recebe o 54 centrado (se não bater em porta) e a outra encosta;
    ///    na outra fiada a que chega atravessa até a face de fora e a contínua é cortada (1 cm de cada lado);
    ///  - portas cortam a alvenaria (medida nominal + 11), janelas não cortam a 1ª e a 2ª fiada;
    ///  - quem fica por cima em cada encontro e a posição exata de cada porta (até alguns cm) são escolhidos
    ///    para todas as paredes fecharem com o mínimo de peças especiais (é o que o engenheiro faz à mão).
    /// </summary>
    public static class PlanejadorFiadas
    {
        private const long Penalidade = 10_000_000; // trecho que não fecha
        private const long PreferenciaVertical = 100; // desempate: nos T e cruzes, a vertical por cima na 1ª fiada
        private const int Tentativas = 40;            // partidas sorteadas da busca, além das três fixas

        private sealed class Corte
        {
            public double A, B;
            public TipoPonta Antes;   // ponta do trecho que termina em A
            public TipoPonta Depois;  // ponta do trecho que começa em B
        }

        // ------------------------------------------------------------------ escolha dos encontros e portas

        /// <summary>
        /// Planeja as duas fiadas do pavimento. Começa com a parede maior por cima em cada encontro e melhora
        /// trocando encontros e deslocando portas enquanto o total de peças especiais diminuir.
        /// </summary>
        /// <param name="fixos">Encontros com estado já decidido (ex.: lido da 1ª fiada desenhada): vertical por cima na 1ª?</param>
        public static SolucaoPavimento Resolver(PlantaDetectada planta, Modulador modulador, OpcoesFiadas op,
            IDictionary<Encontro, bool> fixos = null, bool moverPortas = true)
        {
            var sol = new SolucaoPavimento();
            var cache = new Dictionary<(long, int, int), int?>();
            int? Custo(TrechoFiada t)
            {
                var p = t.Pontas;
                int bordas = (int)p.SemCompensador | (int)p.Porta << 2 | (int)p.Encontro << 4 | (int)p.Passa << 6 | (int)p.Canto << 8;
                var k = ((long)Math.Round(t.Comprimento * 100), bordas, (int)p.Ajuste);
                if (!cache.TryGetValue(k, out var c)) cache[k] = c = modulador.Custo(t.Comprimento, p);
                return c;
            }

            var reais = planta.Encontros.Where(e => e.Tipo != TipoEncontro.Outro).ToList();
            var boneca = reais.ToDictionary(e => e, e => BonecaNoT(e, op));
            var portas = planta.Paredes.SelectMany(w => w.VaosQueCortam().Select(v => (w, v))).ToList();

            // Apartamentos espelhados: encontros e portas espelhados são decididos juntos (resultado simétrico).
            var simetria = Simetria.Detectar(planta);
            if (simetria.Eixos.Count > 0)
                sol.Avisos.Add("Pavimento simétrico em " + string.Join(" e ", simetria.Eixos) +
                               ": paredes espelhadas recebem a modulação espelhada.");
            var grupos = simetria.Encontros.Where(g => g[0].Tipo != TipoEncontro.Outro).ToList();
            var gruposPorta = simetria.Portas;

            Dictionary<Encontro, EstadoEncontro> melhorEstados = null;
            Dictionary<Vao, double> melhorDesloc = null;
            long melhorTotal = long.MaxValue;

            // Partidas: a parede maior por cima, todas verticais, todas horizontais e algumas sorteadas (semente
            // fixa: o resultado é sempre o mesmo para o mesmo desenho).
            var sorteio = new Random(1234);
            var partidas = new List<Func<Encontro, bool>>
            {
                e => !(e.Dominante(1)?.Horizontal ?? true),
                e => true,
                e => false
            };
            for (int i = 0; i < Tentativas; i++)
            {
                var escolha = new Dictionary<Encontro, bool>();
                foreach (var g in grupos)
                {
                    bool v = sorteio.Next(2) == 0;
                    foreach (var e in g) escolha[e] = v;
                }
                partidas.Add(e => escolha.TryGetValue(e, out var v) && v);
            }

            foreach (var partida in partidas)
            {
                var estados = planta.Encontros.ToDictionary(e => e, e => new EstadoEncontro(e));
                foreach (var g in simetria.Encontros)
                    foreach (var e in g) estados[e].VerticalPorCima = partida(g[0]);
                if (fixos != null)
                    foreach (var kv in fixos)
                        if (estados.TryGetValue(kv.Key, out var est)) { est.VerticalPorCima = kv.Value; est.Fixo = true; }
                foreach (var gp in gruposPorta)
                    foreach (var (_, v, _, desvio) in gp) v.Deslocamento = desvio;

                var memoria = new Dictionary<(Parede, string), long>();
                long CustoParedeMemo(Parede w)
                {
                    var chave = (w, Assinatura(planta, w, estados));
                    if (!memoria.TryGetValue(chave, out long c)) memoria[chave] = c = CustoParede(planta, w, op, estados, Custo);
                    return c;
                }
                var custoParede = planta.Paredes.ToDictionary(w => w, w => CustoParedeMemo(w));
                long total = custoParede.Values.Sum() + reais.Sum(e => Preferencia(estados[e], boneca[e]));

                // Recalcula as paredes dadas e devolve a variação do total.
                long Recalcular(IEnumerable<Parede> paredes)
                {
                    long delta = 0;
                    foreach (var w in paredes)
                    {
                        long novo = CustoParedeMemo(w);
                        delta += novo - custoParede[w];
                        custoParede[w] = novo;
                    }
                    return delta;
                }

                // Melhor posição de um grupo de portas espelhadas, com o resto parado; devolve a variação do total.
                long MoverPortas(List<(Parede parede, Vao vao, int sinal, double delta)> grupo)
                {
                    if (!moverPortas || op.DeslocamentoMaximoPorta <= 0) return 0;
                    var (w0, v0, _, _) = grupo[0];
                    var afetadas = Vizinhas(planta, grupo.Select(g => g.parede));
                    double atual = v0.Deslocamento;
                    var antes = afetadas.ToDictionary(x => x, x => custoParede[x]);
                    long melhorDelta = 0;
                    double melhorS = atual;
                    foreach (double s in Candidatos(planta, w0, v0, op))
                    {
                        if (Math.Abs(s - atual) < 1e-9) continue;
                        foreach (var (_, v, sinal, desvio) in grupo) v.Deslocamento = s * sinal + desvio;
                        long delta = 0;
                        foreach (var x in afetadas) delta += CustoParedeMemo(x) - antes[x];
                        // Desempate: o menor deslocamento.
                        if (delta < melhorDelta || (delta == melhorDelta && delta < 0 && Math.Abs(s) < Math.Abs(melhorS)))
                        {
                            melhorDelta = delta;
                            melhorS = s;
                        }
                    }
                    foreach (var (_, v, sinal, desvio) in grupo) v.Deslocamento = melhorS * sinal + desvio;
                    return Math.Abs(melhorS - atual) > 1e-9 ? Recalcular(afetadas) : 0;
                }

                for (int passada = 0; passada < 30; passada++)
                {
                    bool melhorou = false;
                    foreach (var g in grupos)
                    {
                        if (g.Any(e => estados[e].Fixo)) continue;
                        var afetadas = Vizinhas(planta, g.SelectMany(e => e.Paredes));
                        var custosAntes = afetadas.ToDictionary(w => w, w => custoParede[w]);
                        var portasAqui = gruposPorta.Where(gp => gp.Any(p => afetadas.Contains(p.parede))).ToList();
                        var deslocAntes = portasAqui.SelectMany(gp => gp).ToDictionary(p => p.vao, p => p.vao.Deslocamento);

                        long prefAntes = g.Sum(e => Preferencia(estados[e], boneca[e]));
                        foreach (var e in g) estados[e].VerticalPorCima = !estados[e].VerticalPorCima;
                        long delta = Recalcular(afetadas) + g.Sum(e => Preferencia(estados[e], boneca[e])) - prefAntes;
                        // Trocar o encontro pode pedir outra posição para as portas vizinhas.
                        foreach (var gp in portasAqui) delta += MoverPortas(gp);

                        if (delta < 0) { total += delta; melhorou = true; continue; }

                        foreach (var e in g) estados[e].VerticalPorCima = !estados[e].VerticalPorCima;
                        foreach (var kv in deslocAntes) kv.Key.Deslocamento = kv.Value;
                        foreach (var w in Vizinhas(planta, afetadas))
                            custoParede[w] = custosAntes.TryGetValue(w, out var c) ? c : CustoParedeMemo(w);
                    }

                    foreach (var gp in gruposPorta)
                    {
                        long d = MoverPortas(gp);
                        if (d < 0) { total += d; melhorou = true; }
                    }
                    if (!melhorou) break;
                }
                if (total < melhorTotal)
                {
                    melhorTotal = total;
                    melhorEstados = estados;
                    melhorDesloc = portas.ToDictionary(p => p.v, p => p.v.Deslocamento);
                }
            }

            foreach (var kv in melhorDesloc) kv.Key.Deslocamento = kv.Value;
            sol.Custo = melhorTotal;
            sol.Estados.AddRange(melhorEstados.Values);
            sol.Fiada1 = Planejar(planta, 1, op, melhorEstados, sol.Avisos);
            sol.Fiada2 = Planejar(planta, 2, op, melhorEstados, null);
            foreach (var e in reais.Where(e => !Inativo(planta, e, op)))
                sol.Grautes.Add((e.X, e.Y));
            return sol;
        }

        /// <summary>
        /// Deslocamentos da porta que deixam as ombreiras na grade modular (os trechos dos dois lados fecham
        /// com juntas de 1 cm): na grade de 5 cm, os pedaços de parede terminam sempre na mesma posição módulo 5.
        /// Sem grade, todos os deslocamentos de meio em meio centímetro.
        /// </summary>
        private static IEnumerable<double> Candidatos(PlantaDetectada planta, Parede w, Vao v, OpcoesFiadas op)
        {
            double max = op.DeslocamentoMaximoPorta;
            var grade = planta.Grade;
            if (grade == null)
            {
                for (double s = -max; s <= max + 1e-9; s += 0.5) yield return s;
                yield break;
            }
            var (modulo, refX, refY) = grade.Value;
            double referencia = w.Horizontal ? refX : refY;            // eixos das paredes que cortam esta
            double fimDeTrecho = referencia - op.Largura / 2 - op.Junta; // ex.: eixo − 8
            var (a, _) = v.Alvenaria(v.Tipo == TipoVao.Porta ? op.FolgaPorta : 0);
            double a0 = a - v.Deslocamento;
            double r = ((fimDeTrecho - a0) % modulo + modulo) % modulo;  // deslocamento que põe a ombreira na grade
            yield return 0;
            for (double s = r - 3 * modulo; s <= max + 1e-9; s += modulo)
                if (s >= -max - 1e-9) yield return s;
        }

        /// <summary>O que define o custo de uma parede: estados dos encontros dela e posição das portas por perto.</summary>
        private static string Assinatura(PlantaDetectada planta, Parede w, IReadOnlyDictionary<Encontro, EstadoEncontro> estados)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var e in planta.EncontrosDe(w))
            {
                sb.Append(estados.TryGetValue(e, out var est) && est.VerticalPorCima ? 'V' : 'H');
                foreach (var o in e.Paredes)
                    foreach (var v in o.VaosQueCortam())
                        sb.Append(v.Deslocamento.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            }
            foreach (var v in w.VaosQueCortam())
                sb.Append('|').Append(v.Deslocamento.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>
        /// Desempate quando as duas escolhas gastam as mesmas peças: o engenheiro põe a parede vertical por
        /// cima na 1ª fiada nos encontros em T e em cruz (o 54 da horizontal fica na 2ª fiada).
        /// </summary>
        private static long Preferencia(EstadoEncontro e, bool continuaPorCima)
        {
            var en = e.Encontro;
            if (en.Tipo != TipoEncontro.T && en.Tipo != TipoEncontro.X) return 0;
            if (continuaPorCima && en.Tipo == TipoEncontro.T && en.Passantes.Count > 0)
                return e.VerticalPorCima == !en.Passantes[0].Horizontal ? 0 : PreferenciaVertical;
            return e.VerticalPorCima ? 0 : PreferenciaVertical;
        }

        /// <summary>
        /// T em que a parede que chega é só uma "boneca" ao lado de porta (a porta começa a menos de 30 cm da
        /// face da contínua): o engenheiro põe o 54 da contínua na 1ª fiada e a boneca atravessa na 2ª com o 19.
        /// </summary>
        private static bool BonecaNoT(Encontro e, OpcoesFiadas op)
        {
            if (e.Tipo != TipoEncontro.T) return false;
            foreach (var w in e.Terminais)
            {
                double t = w.AoLongo(e.X, e.Y);
                bool noInicio = Math.Abs(t - w.Inicio) <= Math.Abs(t - w.Fim);
                double face = noInicio ? t + op.Largura / 2 : t - op.Largura / 2;
                foreach (var v in w.VaosQueCortam())
                {
                    var (a, b) = v.Alvenaria(v.Tipo == TipoVao.Porta ? op.FolgaPorta : 0);
                    if (noInicio ? a - face < 30 : face - b < 30) return true;
                }
            }
            return false;
        }

        /// <summary>A parede e as que dividem encontros com ela (uma porta perto do encontro muda o lado de lá).</summary>
        private static List<Parede> Vizinhas(PlantaDetectada planta, IEnumerable<Parede> paredes)
        {
            var r = new List<Parede>();
            foreach (var w in paredes)
            {
                if (!r.Contains(w)) r.Add(w);
                foreach (var e in planta.EncontrosDe(w))
                    foreach (var o in e.Paredes)
                        if (!r.Contains(o)) r.Add(o);
            }
            return r;
        }

        private static long CustoParede(PlantaDetectada planta, Parede w, OpcoesFiadas op,
            IReadOnlyDictionary<Encontro, EstadoEncontro> estados, Func<TrechoFiada, int?> custo)
        {
            long total = 0;
            foreach (int fiada in new[] { 1, 2 })
            {
                var plano = new PlanoFiada();
                var curtos = new List<string>();
                PlanejarParede(planta, w, fiada, op, estados, plano, curtos);
                foreach (var t in plano.Trechos)
                    total += custo(t) ?? Penalidade;
                total += curtos.Count(a => a.Contains("curto demais")) * Penalidade;
                total += curtos.Count(a => a.Contains("dobradiça")) * (Penalidade / 5);
            }
            return total;
        }

        // ------------------------------------------------------------------ uma fiada

        /// <summary>Trechos de uma fiada para os estados de encontro dados (nulo = a parede maior por cima na 1ª).</summary>
        public static PlanoFiada Planejar(PlantaDetectada planta, int fiada, OpcoesFiadas op,
            IReadOnlyDictionary<Encontro, EstadoEncontro> estados = null, List<string> avisos = null)
        {
            estados = estados ?? planta.Encontros.ToDictionary(e => e,
                e => new EstadoEncontro(e) { VerticalPorCima = !(e.Dominante(1)?.Horizontal ?? true) });
            var plano = new PlanoFiada();
            foreach (var w in planta.Paredes)
                PlanejarParede(planta, w, fiada, op, estados, plano, avisos);
            return plano;
        }

        /// <summary>
        /// Encontro "inativo": a porta de uma parede que chega vai até a face da outra (não sobra alvenaria
        /// entre a porta e o encontro). Aí não há amarração: a outra parede passa contínua nas duas fiadas.
        /// </summary>
        private static bool Inativo(PlantaDetectada planta, Encontro e, OpcoesFiadas op)
        {
            foreach (var w in e.Terminais)
            {
                double t = w.AoLongo(e.X, e.Y);
                bool noInicio = Math.Abs(t - w.Inicio) <= Math.Abs(t - w.Fim);
                double face = noInicio ? t + op.Largura / 2 : t - op.Largura / 2;
                double folgaMinima = op.Junta + op.TrechoMinimo - 0.25; // nem a bolacha de 4 cabe
                foreach (var v in w.VaosQueCortam())
                {
                    var (a, b) = v.Alvenaria(v.Tipo == TipoVao.Porta ? op.FolgaPorta : 0);
                    if (noInicio ? a < face + folgaMinima : b > face - folgaMinima) return true;
                }
            }
            return false;
        }

        private static void PlanejarParede(PlantaDetectada planta, Parede w, int fiada, OpcoesFiadas op,
            IReadOnlyDictionary<Encontro, EstadoEncontro> estados, PlanoFiada plano, List<string> avisos)
        {
            double meia = op.Largura / 2, recuo = meia + op.Junta;
            double inicio = w.Inicio, fim = w.Fim;
            var tIni = TipoPonta.Solta;
            var tFim = TipoPonta.Solta;
            var cortes = new List<Corte>();
            var centros54 = new List<double>();

            var portas = w.VaosQueCortam()
                .Select(v => v.Alvenaria(v.Tipo == TipoVao.Porta ? op.FolgaPorta : 0))
                .ToList();

            foreach (var e in planta.EncontrosDe(w))
            {
                double t = w.AoLongo(e.X, e.Y);
                bool terminal = e.Terminais.Contains(w);
                bool noInicio = Math.Abs(t - w.Inicio) <= Math.Abs(t - w.Fim);

                if (e.Tipo == TipoEncontro.Outro)
                {
                    // Encontro que o detector não reconheceu: a parede continua ali (sem 19 nem bolacha na ponta),
                    // mas a amarração fica para o projetista.
                    if (!terminal) continue;
                    if (noInicio) tIni = TipoPonta.Encosta; else tFim = TipoPonta.Encosta;
                    if (fiada == 1)
                        avisos?.Add($"{w.Nome}: encontro não reconhecido em ({e.X:0.#}; {e.Y:0.#}); conferir a amarração.");
                    continue;
                }

                bool inativo = Inativo(planta, e, op);
                bool porCima = estados.TryGetValue(e, out var est) && est.VerticalPorCimaNa(fiada) == !w.Horizontal;

                if (terminal)
                {
                    // Porta que vai até a face da outra parede: esta parede termina na porta; a outra passa.
                    double folgaMinima = op.Junta + op.TrechoMinimo - 0.25;
                    bool minhaPortaNoEncontro = inativo && portas.Any(p => noInicio ? p.a < t + meia + folgaMinima : p.b > t - meia - folgaMinima);
                    if (inativo && !minhaPortaNoEncontro) porCima = true;   // a outra é que termina na porta
                    if (minhaPortaNoEncontro) porCima = false;
                    double novo = porCima ? (noInicio ? t - meia : t + meia) : (noInicio ? t + recuo : t - recuo);
                    var tipo = porCima ? TipoPonta.Passa : e.Tipo == TipoEncontro.L ? TipoPonta.EncostaCanto : TipoPonta.Encosta;
                    // Porta encostada na outra parede do lado da dobradiça: sem alvenaria para o marco.
                    if (minhaPortaNoEncontro && w.VaosQueCortam().Any(v => v.LadoDobradica == (noInicio ? -1 : 1) &&
                            (noInicio ? v.Alvenaria(op.FolgaPorta).a < t + meia + folgaMinima : v.Alvenaria(op.FolgaPorta).b > t - meia - folgaMinima)))
                        avisos?.Add($"{w.Nome}: porta encostada na parede do lado da dobradiça (sem alvenaria para o marco).");
                    if (noInicio) { inicio = novo; tIni = tipo; }
                    else { fim = novo; tFim = tipo; }
                }
                else if (inativo || porCima)
                {
                    if (!inativo && op.BlocoEncontro != null && (e.Tipo == TipoEncontro.T || e.Tipo == TipoEncontro.X))
                        centros54.Add(t);
                }
                else
                {
                    var tipoCorte = e.Tipo == TipoEncontro.X ? TipoPonta.Cruza : TipoPonta.Corte;
                    cortes.Add(new Corte { A = t - recuo, B = t + recuo, Antes = tipoCorte, Depois = tipoCorte });
                }
            }

            // Portas e passagens cortam a alvenaria: medida nominal + folga de marco.
            foreach (var (a, b) in portas)
                cortes.Add(new Corte { A = a, B = b, Antes = TipoPonta.Porta, Depois = TipoPonta.Porta });

            // 54 fixo centrado no T/cruz, se não bater em porta. Encontros muito próximos (parede dupla) dividem um.
            foreach (var grupo in AgruparEncontros(centros54, op))
            {
                double l54 = op.BlocoEncontro.Comprimento;
                double min = grupo.Min(), max = grupo.Max(), c = (min + max) / 2;
                double a = c - l54 / 2, b = c + l54 / 2;
                bool cobre = (max - min) + op.Largura + 2 * op.Junta <= l54;
                bool bate = portas.Any(p => p.a < b + op.Junta && p.b > a - op.Junta);
                if (!cobre || bate || a < inicio - 0.01 || b > fim + 0.01)
                {
                    if (!bate && fiada == 1)
                        avisos?.Add($"{w.Nome}: o 54 do encontro em {c:0.#} não coube (encontros muito próximos ou perto da ponta); parede contínua ali.");
                    continue;
                }
                plano.Fixas.Add(new PecaFixa { Parede = w, Peca = op.BlocoEncontro, Inicio = a });
                cortes.Add(new Corte { A = a - op.Junta, B = b + op.Junta, Antes = TipoPonta.Encosta, Depois = TipoPonta.Encosta });
            }

            // Intervalos que sobram, com o que há em cada ponta.
            var intervalos = new List<(double a, double b, TipoPonta pa, TipoPonta pb)>();
            double cursor = inicio;
            var cursorPonta = tIni;
            foreach (var c in cortes.OrderBy(c => c.A))
            {
                if (c.B <= cursor) continue;
                if (c.A > cursor)
                    intervalos.Add((cursor, Math.Min(c.A, fim), cursorPonta, c.Antes));
                cursor = Math.Max(cursor, c.B);
                cursorPonta = c.Depois;
                if (cursor >= fim) break;
            }
            if (cursor < fim)
                intervalos.Add((cursor, fim, cursorPonta, tFim));

            double centro = Centro(planta, w);
            foreach (var iv in intervalos)
            {
                double comp = iv.b - iv.a;
                if (comp < op.TrechoMinimo)
                {
                    if (comp > 0.5)
                        avisos?.Add($"{w.Nome} ({fiada}ª fiada): trecho de {comp:0.#} cm entre {iv.a:0.#} e {iv.b:0.#} é curto demais e foi ignorado.");
                    continue;
                }
                plano.Trechos.Add(new TrechoFiada
                {
                    Parede = w,
                    Fiada = fiada,
                    Inicio = iv.a,
                    Fim = iv.b,
                    TipoInicio = iv.pa,
                    TipoFim = iv.pb,
                    Pontas = ParaPontas(iv.pa, iv.pb, Math.Abs(iv.a - centro) <= Math.Abs(iv.b - centro) + 5, fiada),
                    Nome = w.Nome ?? $"#{w.Indice}"
                });
            }
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

        /// <summary>Centro do pavimento (cm) ao longo do eixo da parede.</summary>
        private static double Centro(PlantaDetectada planta, Parede w)
        {
            var mesmas = planta.Paredes.Where(p => p.Orientacao != w.Orientacao).Select(p => p.Coord).ToList();
            return mesmas.Count == 0 ? (w.Inicio + w.Fim) / 2 : (mesmas.Min() + mesmas.Max()) / 2;
        }

        /// <summary>
        /// Tipo de cada ponta → regras do motor. Na 1ª fiada as peças de ajuste (34) vão juntas para uma ponta,
        /// como o engenheiro faz: a ponta em que a parede passa; senão a cortada pela parede que chega no T;
        /// senão a que encosta. Empate: a ponta mais perto do centro do pavimento (paredes espelhadas ficam
        /// espelhadas). Na 2ª fiada quem manda é a amarração com a 1ª; no empate, os 34 ficam nas pontas.
        /// </summary>
        /// <param name="inicioMaisCentral">A ponta inicial está mais perto do centro do pavimento (ou empata).</param>
        public static Pontas ParaPontas(TipoPonta inicio, TipoPonta fim, bool inicioMaisCentral = true, int fiada = 1)
        {
            var p = new Pontas();
            void Marca(TipoPonta t, Bordas lado)
            {
                switch (t)
                {
                    case TipoPonta.Solta:
                    case TipoPonta.Porta:
                        p.Porta |= lado;
                        p.SemCompensador |= lado;
                        break;
                    case TipoPonta.Passa:
                        p.Encontro |= lado;
                        p.Passa |= lado;
                        p.SemCompensador |= lado;
                        break;
                    case TipoPonta.EncostaCanto:
                        p.Encontro |= lado;
                        p.Canto |= lado;
                        break;
                    default:
                        p.Encontro |= lado;
                        break;
                }
            }
            Marca(inicio, Bordas.Inicio);
            Marca(fim, Bordas.Fim);

            int Prioridade(TipoPonta t) => t == TipoPonta.Passa ? 0 : t == TipoPonta.Corte ? 1
                : t == TipoPonta.Encosta || t == TipoPonta.EncostaCanto || t == TipoPonta.Cruza ? 2 : 9;
            int pi = Prioridade(inicio), pf = Prioridade(fim);
            if (pi < 9 && pf < 9 && (fiada % 2 == 0 || (pi == 0 && pf == 0)))
                p.Ajuste = Bordas.Ambas;                 // nas duas pontas (cada ponta que passa leva o seu 34)
            else if (pi < 9 || pf < 9)
                p.Ajuste = pf < pi || (pf == pi && !inicioMaisCentral) ? Bordas.Fim : Bordas.Inicio;
            return p;
        }

        // ------------------------------------------------------------------ juntas da fiada de baixo

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
    /// Grade modular: o engenheiro trabalha com os eixos das paredes a distâncias múltiplas de 5 cm (é o que faz
    /// todos os trechos fecharem com juntas de 1 cm). A arquitetura vem com diferenças de alguns milímetros;
    /// aqui cada eixo vai para a posição da grade mais próxima (no máximo 2,5 cm).
    /// </summary>
    public static class GradeModular
    {
        /// <returns>O maior deslocamento aplicado (cm).</returns>
        public static double Ajustar(PlantaDetectada planta, double modulo = 5, double largura = 14)
        {
            if (planta.Paredes.Count == 0) return 0;
            var mapaX = Mapa(planta.Paredes.Where(p => !p.Horizontal).Select(p => p.Coord), modulo, out double refX);
            var mapaY = Mapa(planta.Paredes.Where(p => p.Horizontal).Select(p => p.Coord), modulo, out double refY);
            double maior = 0;

            double Snap(Dictionary<double, double> mapa, double referencia, double v)
            {
                foreach (var kv in mapa)
                    if (Math.Abs(kv.Key - v) <= 1.0) return kv.Value;
                return double.NaN;
            }
            double Grade(double referencia, double v) => referencia + Math.Round((v - referencia) / modulo) * modulo;

            foreach (var w in planta.Paredes)
            {
                var (meu, refMeu) = w.Horizontal ? (mapaY, refY) : (mapaX, refX);
                var (outro, refOutro) = w.Horizontal ? (mapaX, refX) : (mapaY, refY);
                double nova = Snap(meu, refMeu, w.Coord);
                maior = Math.Max(maior, Math.Abs(nova - w.Coord));
                w.Coord = nova;

                // Pontas no eixo de outra parede vão junto com ele; pontas soltas ficam na face da grade (eixo ± 7).
                double ini = Snap(outro, refOutro, w.Inicio);
                w.Inicio = double.IsNaN(ini) ? Grade(refOutro, w.Inicio + largura / 2) - largura / 2 : ini;
                double fim = Snap(outro, refOutro, w.Fim);
                w.Fim = double.IsNaN(fim) ? Grade(refOutro, w.Fim - largura / 2) + largura / 2 : fim;
            }
            foreach (var e in planta.Encontros)
            {
                double x = Snap(mapaX, refX, e.X), y = Snap(mapaY, refY, e.Y);
                if (!double.IsNaN(x)) e.X = x;
                if (!double.IsNaN(y)) e.Y = y;
            }
            planta.Grade = (modulo, refX, refY);
            return maior;
        }

        private static Dictionary<double, double> Mapa(IEnumerable<double> coords, double modulo, out double referencia)
        {
            var lista = coords.OrderBy(c => c).ToList();
            referencia = lista.Count > 0 ? lista[0] : 0;
            var mapa = new Dictionary<double, double>();
            foreach (var c in lista)
                if (!mapa.ContainsKey(c))
                    mapa[c] = referencia + Math.Round((c - referencia) / modulo) * modulo;
            return mapa;
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
            foreach (var p in Ordem(planta.Paredes))
                p.Nome = prefixo + n++;
        }

        /// <summary>
        /// Numeração por tipo ("Modulação TIPOS" do engenheiro): paredes com a mesma modulação nas duas
        /// fiadas (lida de qualquer um dos lados) recebem o mesmo número.
        /// </summary>
        /// <param name="assinatura">Sequência de peças da parede nas duas fiadas, de uma ponta à outra, e ao contrário.</param>
        public static void NumerarPorTipo(IEnumerable<Parede> paredes, Func<Parede, (string ida, string volta)> assinatura,
            string prefixo = "PAREDE ")
        {
            var tipos = new Dictionary<string, int>();
            int n = 1;
            foreach (var p in Ordem(paredes))
            {
                var (ida, volta) = assinatura(p);
                string chave = string.CompareOrdinal(ida, volta) <= 0 ? ida : volta;
                if (!tipos.TryGetValue(chave, out int numero)) tipos[chave] = numero = n++;
                p.Nome = prefixo + numero;
            }
        }

        /// <summary>
        /// Modulação da parede nas duas fiadas (peças e 54 fixos, de uma ponta à outra), lida nos dois sentidos:
        /// duas paredes são do mesmo tipo quando uma das leituras é igual.
        /// </summary>
        public static (string ida, string volta) Assinatura(SolucaoPavimento sol, ModulacaoFiadas mod, Parede w)
        {
            string Seq(int f, bool inverso)
            {
                var pecas = mod.Fiada(f).Where(x => x.trecho.Parede == w)
                    .SelectMany(x => x.resultado.Pecas.Select(pc => (a: x.trecho.Inicio + pc.Inicio, b: x.trecho.Inicio + pc.Fim, c: pc.Peca.Codigo)))
                    .Concat(sol.Plano(f).Fixas.Where(x => x.Parede == w).Select(x => (a: x.Inicio, b: x.Fim, c: x.Peca.Codigo)))
                    .Select(x => inverso ? (a: w.Inicio + w.Fim - x.b, x.c) : (a: x.a, x.c))
                    .OrderBy(x => x.a)
                    .Select(x => $"{x.c}@{Math.Round(x.a - w.Inicio)}");
                return string.Join(",", pecas);
            }
            string Vaos(bool inverso) => string.Join(",", w.Vaos.Select(v => (inverso ? w.Inicio + w.Fim - v.Centro : v.Centro) - w.Inicio)
                .Select(c => Math.Round(c).ToString(System.Globalization.CultureInfo.InvariantCulture)).OrderBy(c => c));
            return ($"{Math.Round(w.Comprimento)}|{Seq(1, false)}|{Seq(2, false)}|{Vaos(false)}",
                    $"{Math.Round(w.Comprimento)}|{Seq(1, true)}|{Seq(2, true)}|{Vaos(true)}");
        }

        private static IEnumerable<Parede> Ordem(IEnumerable<Parede> paredes)
        {
            var lista = paredes.ToList();
            return lista.Where(p => p.Horizontal).OrderByDescending(p => Math.Round(p.Coord, 0)).ThenBy(p => p.Inicio)
                .Concat(lista.Where(p => !p.Horizontal).OrderBy(p => Math.Round(p.Coord, 0)).ThenByDescending(p => p.Fim));
        }
    }
}
