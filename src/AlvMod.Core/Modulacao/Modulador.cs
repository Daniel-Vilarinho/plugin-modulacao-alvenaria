using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AlvMod.Core.Catalogo;

namespace AlvMod.Core.Modulacao
{
    /// <summary>Pontas de um trecho: <see cref="Inicio"/> = ponta esquerda/inferior, <see cref="Fim"/> = direita/superior.</summary>
    [Flags]
    public enum Bordas
    {
        Nenhuma = 0,
        Inicio = 1,
        Fim = 2,
        Ambas = Inicio | Fim
    }

    /// <summary>
    /// O que existe em cada ponta de um trecho de parede. As regras de posição das peças dependem disso
    /// (correções do engenheiro):
    ///  - nunca bolacha (4 e 9) em ponta de porta, encontro ou canto (precisa de septo para o graute);
    ///  - o 19 só na ponta de parede SEM continuidade (porta ou parede solta) — nunca em encontro;
    ///    na ponta que encosta em porta ele é obrigatório;
    ///  - no canto (L) e na parede que chega num T: a parede que passa termina em 34 e a que encosta
    ///    começa com 39 (defasagem de meio bloco entre as fiadas);
    ///  - o 54 não é escolhido pelo motor: é peça fixa, centrada nos encontros em T e em cruz.
    /// </summary>
    public sealed class Pontas
    {
        /// <summary>Pontas em porta, encontro ou canto: sem bolacha.</summary>
        public Bordas SemCompensador { get; set; }

        /// <summary>Pontas que encostam em porta/passagem: a última peça é o meio bloco (19).</summary>
        public Bordas Porta { get; set; }

        /// <summary>Pontas que encostam em outra parede (canto, T, cruz, bloco 54): a parede continua ali, o 19 não entra.</summary>
        public Bordas Encontro { get; set; }

        /// <summary>Pontas de parede que passa direto no canto ou atravessa o T: a peça da ponta é o 34.</summary>
        public Bordas Passa34 { get; set; }

        /// <summary>Pontas que encostam na parede que passa (ou no 54 do encontro): a peça da ponta é o 39.</summary>
        public Bordas Exige39 { get; set; }

        public static Pontas Nenhuma => new Pontas();

        internal Pontas Espelhado() => new Pontas
        {
            SemCompensador = Espelhar(SemCompensador),
            Porta = Espelhar(Porta),
            Encontro = Espelhar(Encontro),
            Passa34 = Espelhar(Passa34),
            Exige39 = Espelhar(Exige39)
        };

        /// <summary>Tira as exigências de peça da ponta onde fica a sobra (ela não encosta de verdade na outra parede).</summary>
        internal Pontas SemExigenciasDoLado(Bordas lado) => new Pontas
        {
            SemCompensador = SemCompensador,
            Porta = Porta,
            Encontro = Encontro,
            Passa34 = Passa34 & ~lado,
            Exige39 = Exige39 & ~lado
        };

        internal static Bordas Espelhar(Bordas b)
        {
            var r = Bordas.Nenhuma;
            if (b.HasFlag(Bordas.Inicio)) r |= Bordas.Fim;
            if (b.HasFlag(Bordas.Fim)) r |= Bordas.Inicio;
            return r;
        }

        /// <summary>Ponta "dura": encosta em outra parede, onde não pode sobrar espaço (a junta tem de ser 1 cm).</summary>
        public bool Dura(Bordas lado) => Encontro.HasFlag(lado);
    }

    public sealed class OpcoesModulacao
    {
        /// <summary>Diferença máxima (cm) aceita entre o comprimento medido e um comprimento modulável.</summary>
        public double Tolerancia { get; set; } = 0.5;

        /// <summary>
        /// Distância mínima (cm) entre juntas de fiadas consecutivas. Questionário (item 18): por norma,
        /// no mínimo 1/4 do bloco (39/4 ≈ 10 cm).
        /// </summary>
        public double SobreposicaoMinima { get; set; } = 10;

        /// <summary>Defasagem ideal (cm): meio bloco. Abaixo dela o motor aplica penalidade.</summary>
        public double SobreposicaoIdeal { get; set; } = 20;

        /// <summary>Penalidade por cm abaixo da defasagem ideal (na mesma escala dos custos das peças).</summary>
        public double PenalidadePorCm { get; set; } = 1;
    }

    public sealed class PecaPosicionada
    {
        public Peca Peca { get; set; }

        /// <summary>Início da peça, em cm a partir do início do trecho.</summary>
        public double Inicio { get; set; }

        public double Fim => Inicio + Peca.Comprimento;
    }

    public sealed class ResultadoModulacao
    {
        /// <summary>O trecho fechou (com, no máximo, a tolerância distribuída nas juntas).</summary>
        public bool Sucesso { get; set; }

        /// <summary>Não fechou, mas as peças foram colocadas e sobrou <see cref="Sobra"/>.</summary>
        public bool Parcial => !Sucesso && Pecas.Count > 0;

        public string Mensagem { get; set; }
        public double ComprimentoMedido { get; set; }

        /// <summary>Comprimento (cm) não preenchido quando o resultado é parcial.</summary>
        public double Sobra { get; set; }

        /// <summary>A sobra fica na ponta inicial (as peças foram ancoradas na ponta final, que encosta em outra parede).</summary>
        public bool SobraNoInicio { get; set; }

        /// <summary>Peças em ordem crescente de posição.</summary>
        public List<PecaPosicionada> Pecas { get; set; } = new List<PecaPosicionada>();

        public List<int> Sugestoes { get; set; } = new List<int>();

        /// <summary>Regras que foi preciso relaxar para o trecho fechar (devem ir para o relatório).</summary>
        public List<string> Avisos { get; set; } = new List<string>();

        /// <summary>Posições (cm, a partir do início do trecho) do centro de cada junta entre peças.</summary>
        public IEnumerable<double> Juntas() =>
            Pecas.Zip(Pecas.Skip(1), (a, b) => (a.Fim + b.Inicio) / 2);
    }

    /// <summary>
    /// Modulação de um trecho reto de parede por programação dinâmica.
    /// Trabalha numa grade inteira cujo passo é o MDC de (peça + junta) de todas as peças
    /// (5 cm na família 39), para evitar erro de ponto flutuante.
    ///
    /// Prioridade (custo): 39 → 34 → 19 (só em ponta livre) → bolachas. As peças ficam a 1 cm uma da
    /// outra; a diferença de até 0,5 cm do desenho vai para o lado da porta ou é dividida entre as juntas.
    /// </summary>
    public sealed class Modulador
    {
        private const int Inviavel = int.MaxValue / 4;

        private readonly Familia _familia;
        private readonly OpcoesModulacao _opcoes;
        private readonly List<Peca> _pecas;
        private readonly int[] _unidades;
        private readonly int _iMeio;
        private readonly bool _tem34;
        private readonly int _custoPrincipal;

        public int Passo { get; }
        public Familia Familia => _familia;

        public Modulador(Familia familia, OpcoesModulacao opcoes = null)
        {
            _familia = familia ?? throw new ArgumentNullException(nameof(familia));
            _opcoes = opcoes ?? new OpcoesModulacao();

            // O 54 nunca é escolhido pelo motor (é peça fixa dos encontros em T e cruz).
            // Ordem = desempate: mais barata primeiro; entre iguais, a maior.
            _pecas = familia.PecasDeFiada()
                .Where(p => p.Comprimento > 0 && p.Funcao != FuncaoPeca.Encontro)
                .OrderBy(p => p.Custo)
                .ThenByDescending(p => p.Comprimento)
                .ToList();
            if (_pecas.Count == 0)
                throw new InvalidOperationException($"A família {familia.Nome} não tem peças habilitadas.");

            Passo = _pecas.Select(p => p.Comprimento + familia.Junta).Aggregate(Mdc);
            _unidades = _pecas.Select(p => (p.Comprimento + familia.Junta) / Passo).ToArray();
            _iMeio = _pecas.FindIndex(p => p.Funcao == FuncaoPeca.Meio);
            _tem34 = _pecas.Any(p => p.Funcao == FuncaoPeca.Amarracao);
            var principais = _pecas.Where(p => p.Funcao == FuncaoPeca.Principal).Select(p => p.Custo).ToList();
            _custoPrincipal = principais.Count > 0 ? principais.Min() : _pecas.Min(p => p.Custo);
        }

        // ------------------------------------------------------------------ API

        /// <summary>Modula um trecho da 1ª fiada.</summary>
        /// <param name="comprimentoCm">Comprimento do trecho de alvenaria (cm).</param>
        /// <param name="pontas">O que há em cada ponta (porta, encontro…). Nulo = duas pontas livres.</param>
        public ResultadoModulacao PrimeiraFiada(double comprimentoCm, Pontas pontas = null) =>
            Modular(comprimentoCm, null, pontas ?? Pontas.Nenhuma, "1ª fiada");

        /// <summary>Modula um trecho da 2ª fiada evitando junta a prumo com a 1ª.</summary>
        /// <param name="juntasPrimeiraFiada">Centro das juntas da 1ª fiada, em cm a partir do início do trecho.</param>
        public ResultadoModulacao SegundaFiada(double comprimentoCm, IEnumerable<double> juntasPrimeiraFiada, Pontas pontas = null) =>
            Modular(comprimentoCm, (juntasPrimeiraFiada ?? Enumerable.Empty<double>()).ToList(), pontas ?? Pontas.Nenhuma, "2ª fiada");

        /// <summary>Comprimentos que fecham, o mais próximo abaixo e acima do medido.</summary>
        public List<int> ComprimentosValidosProximos(double comprimentoCm, Pontas pontas = null)
        {
            pontas = pontas ?? Pontas.Nenhuma;
            var resultado = new List<int>();
            int n0 = (int)Math.Floor((comprimentoCm + _familia.Junta) / Passo);

            for (int n = n0; n > 0 && n > n0 - 40; n--)
                if (Fecha(n, pontas)) { resultado.Add(n * Passo - _familia.Junta); break; }
            for (int n = n0 + 1; n < n0 + 40; n++)
                if (Fecha(n, pontas)) { resultado.Add(n * Passo - _familia.Junta); break; }

            return resultado.Where(c => Math.Abs(c - comprimentoCm) > 1e-9).ToList();
        }

        private bool Fecha(int n, Pontas pontas) =>
            Resolver(n, null, pontas, 0, true, true) != null || Resolver(n, null, pontas, 0, false, false) != null;

        // ------------------------------------------------------------------ modulação

        private sealed class Tentativa
        {
            public bool Porta19;
            public bool PecasDoEncontro;
            public double Minimo;
            public string Aviso;
        }

        /// <summary>Do mais rigoroso ao mais relaxado; a primeira que fecha vence e o que foi relaxado vira aviso.</summary>
        private List<Tentativa> Tentativas(bool segundaFiada, Pontas p)
        {
            double min = _opcoes.SobreposicaoMinima;
            bool temPorta = (p.Porta != Bordas.Nenhuma) && _iMeio >= 0;
            bool temEncontro = (p.Passa34 | p.Exige39) != Bordas.Nenhuma;
            var lista = new List<Tentativa> { new Tentativa { Porta19 = temPorta, PecasDoEncontro = temEncontro, Minimo = min } };
            if (temEncontro)
                lista.Add(new Tentativa
                {
                    Porta19 = temPorta,
                    PecasDoEncontro = false,
                    Minimo = min,
                    Aviso = "a peça junto ao encontro não pôde ser a do padrão (34 na parede que passa, 39 na que encosta)"
                });
            if (temPorta)
                lista.Add(new Tentativa { Porta19 = false, PecasDoEncontro = false, Minimo = min, Aviso = "a ponta junto à porta não coube em meio bloco (19)" });
            if (segundaFiada)
            {
                if (min > 5)
                    lista.Add(new Tentativa
                    {
                        Minimo = 5,
                        Aviso = $"defasagem entre as fiadas reduzida para 5 cm (o mínimo é {Cm(min)} cm)"
                    });
                lista.Add(new Tentativa { Minimo = 0, Aviso = "junta a prumo inevitável neste trecho" });
            }
            return lista;
        }

        private ResultadoModulacao Modular(double comprimentoCm, List<double> juntas1, Pontas pontas, string nomeFiada)
        {
            var r = new ResultadoModulacao { ComprimentoMedido = comprimentoCm };
            string medido = Cm(comprimentoCm);
            bool segunda = juntas1 != null;
            var tentativas = Tentativas(segunda, pontas);

            int n = (int)Math.Round((comprimentoCm + _familia.Junta) / Passo);
            double modulavel = n * Passo - _familia.Junta;
            if (n > 0 && Math.Abs(modulavel - comprimentoCm) <= _opcoes.Tolerancia)
            {
                foreach (var t in tentativas)
                {
                    var seq = Resolver(n, juntas1, pontas, t.Minimo, t.Porta19, t.PecasDoEncontro);
                    if (seq == null) continue;
                    PosicionarAjustado(r, seq, comprimentoCm - modulavel, pontas);
                    r.Sucesso = true;
                    if (t.Aviso != null) r.Avisos.Add(t.Aviso);
                    r.Mensagem = $"{nomeFiada}: {medido} cm = " + string.Join(" + ", seq.Select(x => x.Codigo)) +
                                 (r.Avisos.Count > 0 ? " (" + string.Join("; ", r.Avisos) + ")" : "");
                    return r;
                }
            }

            // Não fecha: coloca as peças ("a parede chegando") ancoradas na ponta que encosta em outra parede,
            // para os blocos de paredes diferentes ficarem sempre a 1 cm; a sobra vai para o lado da porta/livre.
            r.Sugestoes = ComprimentosValidosProximos(comprimentoCm, pontas);
            bool ancoraNoFim = pontas.Dura(Bordas.Fim) && !pontas.Dura(Bordas.Inicio);
            var p2 = (ancoraNoFim ? pontas.Espelhado() : pontas).SemExigenciasDoLado(Bordas.Fim);
            List<double> j2 = segunda && ancoraNoFim ? juntas1.Select(j => comprimentoCm - j).ToList() : juntas1;

            // Só o que cabe sem invadir a outra parede (sobra >= 0).
            int nMax = (int)Math.Floor((comprimentoCm + _familia.Junta + 1e-6) / Passo);
            for (int k = nMax; k > 0; k--)
            {
                foreach (var t in tentativas)
                {
                    var seq = Resolver(k, j2, p2, t.Minimo, t.Porta19, t.PecasDoEncontro);
                    if (seq == null) continue;

                    double ocupado = k * Passo - _familia.Junta;
                    r.Sobra = comprimentoCm - ocupado;
                    r.SobraNoInicio = ancoraNoFim;
                    if (ancoraNoFim) seq.Reverse();
                    Posicionar(r, seq, ancoraNoFim ? r.Sobra : 0, 0);
                    if (t.Aviso != null) r.Avisos.Add(t.Aviso);
                    string lado = ancoraNoFim ? "no início (junto à porta/ponta livre)"
                                : pontas.Porta.HasFlag(Bordas.Fim) ? "no fim, junto à porta" : "no fim";
                    r.Mensagem = $"{medido} cm não fecha com as peças habilitadas da família {_familia.Nome}; " +
                                 $"peças colocadas e sobra de {Cm(r.Sobra)} cm {lado} para ajuste manual" + SufixoSugestoes(r.Sugestoes);
                    return r;
                }
            }

            r.Mensagem = $"{medido} cm não fecha com as peças habilitadas da família {_familia.Nome}" + SufixoSugestoes(r.Sugestoes);
            return r;
        }

        /// <summary>
        /// Posiciona as peças de um trecho que fechou dentro da tolerância. A diferença (até 0,5 cm) entre o
        /// comprimento medido e o modulado vai para a ponta "mole" (porta/livre); se as duas pontas encostam em
        /// outras paredes, é dividida igualmente entre as juntas e as duas pontas (fica imperceptível).
        /// </summary>
        private void PosicionarAjustado(ResultadoModulacao r, List<Peca> seq, double diferenca, Pontas p)
        {
            if (Math.Abs(diferenca) < 1e-9) { Posicionar(r, seq, 0, 0); return; }
            bool duraIni = p.Dura(Bordas.Inicio), duraFim = p.Dura(Bordas.Fim);
            if (!duraFim) Posicionar(r, seq, 0, 0);                 // diferença fica na ponta final
            else if (!duraIni) Posicionar(r, seq, diferenca, 0);   // peças encostam na ponta final
            else
            {
                double parte = diferenca / (seq.Count + 1);
                Posicionar(r, seq, parte, parte);
            }
        }

        private void Posicionar(ResultadoModulacao r, List<Peca> sequencia, double inicio, double extraPorJunta)
        {
            double x = inicio;
            foreach (var peca in sequencia)
            {
                r.Pecas.Add(new PecaPosicionada { Peca = peca, Inicio = x });
                x += peca.Comprimento + _familia.Junta + extraPorJunta;
            }
        }

        // ------------------------------------------------------------------ programação dinâmica

        /// <summary>
        /// DP de trás para frente: melhor[i] = menor custo para preencher da unidade i até n.
        /// Regras de posição (correções do engenheiro):
        ///  - 19: só em ponta sem continuidade (porta ou livre), nunca no meio nem em encontro;
        ///    na ponta que encosta em porta é obrigatório;
        ///  - ponta em encontro: 34 na parede que passa, 39 na que encosta (quando <paramref name="pecasDoEncontro"/>);
        ///  - 34: segunda prioridade no meio da parede;
        ///  - bolacha (4 e 9): nunca em ponta de porta, encontro ou canto;
        ///  - 2ª fiada: junta a menos da defasagem mínima é proibida e abaixo da ideal é penalizada
        ///    (exceto a junta ao lado do 19 da porta, que é a mesma nas duas fiadas).
        /// </summary>
        private List<Peca> Resolver(int n, List<double> juntas1, Pontas p, double minimo, bool exigirPorta19, bool pecasDoEncontro)
        {
            var melhor = new int[n + 1];
            var escolha = new int[n + 1];
            for (int i = 0; i < n; i++) { melhor[i] = Inviavel; escolha[i] = -1; }
            melhor[n] = 0;

            int u19 = _iMeio >= 0 ? _unidades[_iMeio] : 0;

            for (int i = n - 1; i >= 0; i--)
            {
                for (int k = 0; k < _pecas.Count; k++)
                {
                    int fim = i + _unidades[k];
                    if (fim > n || melhor[fim] >= Inviavel) continue;

                    bool ini = i == 0, ultimo = fim == n;
                    var peca = _pecas[k];
                    int custo = peca.Custo;

                    bool emPorta = (ini && p.Porta.HasFlag(Bordas.Inicio)) || (ultimo && p.Porta.HasFlag(Bordas.Fim));
                    bool emEncontro = (ini && p.Encontro.HasFlag(Bordas.Inicio)) || (ultimo && p.Encontro.HasFlag(Bordas.Fim));
                    bool exige34 = pecasDoEncontro && _tem34 &&
                                   ((ini && p.Passa34.HasFlag(Bordas.Inicio)) || (ultimo && p.Passa34.HasFlag(Bordas.Fim)));
                    bool exige39 = pecasDoEncontro &&
                                   ((ini && p.Exige39.HasFlag(Bordas.Inicio)) || (ultimo && p.Exige39.HasFlag(Bordas.Fim)));

                    switch (peca.Funcao)
                    {
                        case FuncaoPeca.Compensador:
                            if ((ini && p.SemCompensador.HasFlag(Bordas.Inicio)) ||
                                (ultimo && p.SemCompensador.HasFlag(Bordas.Fim)))
                                continue;
                            break;
                        case FuncaoPeca.Meio:
                            if (!ini && !ultimo) continue;  // nunca no meio da parede
                            if (emEncontro) continue;        // nunca onde a parede continua (canto, T, cruz)
                            if (emPorta) custo = _custoPrincipal;
                            break;
                        case FuncaoPeca.Amarracao:
                            if (exige34) custo = _custoPrincipal;
                            break;
                    }

                    // Bolachas e 34 de ajuste vão para o meio da parede, longe dos encontros e portas
                    // (pontos de graute): pequena penalidade até 80 cm das pontas, só muda a posição.
                    if (peca.Funcao == FuncaoPeca.Compensador || (peca.Funcao == FuncaoPeca.Amarracao && !exige34))
                    {
                        int perto = Math.Min(i, n - fim);
                        int limite = 80 / Passo;
                        if (perto < limite) custo += 1 + (limite - perto) * 3 / limite;
                    }

                    if (exigirPorta19 && emPorta && peca.Funcao != FuncaoPeca.Meio) continue;
                    if (exige34 && peca.Funcao != FuncaoPeca.Amarracao) continue;
                    if (exige39 && peca.Funcao != FuncaoPeca.Principal) continue;

                    int custoJunta = 0;
                    if (!ultimo)
                    {
                        custoJunta = CustoJunta(fim, juntas1, p, n, minimo, exigirPorta19, u19);
                        if (custoJunta >= Inviavel) continue;
                    }

                    int total = custo + custoJunta + melhor[fim];
                    if (total < melhor[i]) { melhor[i] = total; escolha[i] = k; }
                }
            }

            if (melhor[0] >= Inviavel) return null;

            var seq = new List<Peca>();
            for (int i = 0; i < n; i += _unidades[escolha[i]])
                seq.Add(_pecas[escolha[i]]);
            return seq;
        }

        private int CustoJunta(int fronteira, List<double> juntas1, Pontas p, int n, double minimo,
            bool exigirPorta19, int u19)
        {
            if (juntas1 == null || juntas1.Count == 0) return 0;

            // O 19 junto à porta é igual nas duas fiadas: a junta ao lado dele coincide por construção.
            if (exigirPorta19 && u19 > 0 &&
                ((p.Porta.HasFlag(Bordas.Inicio) && fronteira == u19) ||
                 (p.Porta.HasFlag(Bordas.Fim) && fronteira == n - u19)))
                return 0;

            double centro = fronteira * Passo - _familia.Junta / 2.0;
            double d = juntas1.Min(j => Math.Abs(j - centro));
            if (d < minimo - 1e-9) return Inviavel;
            return d >= _opcoes.SobreposicaoIdeal ? 0 : (int)Math.Round((_opcoes.SobreposicaoIdeal - d) * _opcoes.PenalidadePorCm);
        }

        private static string Cm(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        private static string SufixoSugestoes(List<int> sugestoes) =>
            sugestoes.Count == 0 ? "." : $". Comprimentos que fecham mais próximos: {string.Join(" ou ", sugestoes)} cm.";

        private static int Mdc(int a, int b) => b == 0 ? a : Mdc(b, a % b);
    }
}
