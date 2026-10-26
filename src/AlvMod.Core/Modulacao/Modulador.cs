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
    /// (método do engenheiro, conferido na modulação dele):
    ///  - no quadrado do encontro vai bloco que recebe graute (39, 34 ou, na boneca, 19): bolacha não;
    ///  - o 19 só na ombreira de porta ou na ponta solta, nunca onde a parede continua;
    ///  - as peças de ajuste (34 e bolachas) ficam juntas, encostadas no encontro (de preferência na ponta em
    ///    que a parede passa), nunca no meio da parede;
    ///  - o 54 não é escolhido pelo motor: é peça fixa, centrada nos encontros em T e em cruz.
    /// </summary>
    public sealed class Pontas
    {
        /// <summary>Pontas sem bolacha: onde a parede passa no encontro, ombreira de porta e ponta solta.</summary>
        public Bordas SemCompensador { get; set; }

        /// <summary>Ombreira de porta/passagem ou ponta solta: o meio bloco (19) pode entrar.</summary>
        public Bordas Porta { get; set; }

        /// <summary>Pontas em que a parede encontra outra (passa, encosta ou é cortada): o 19 não entra.</summary>
        public Bordas Encontro { get; set; }

        /// <summary>Pontas em que a parede passa direto no encontro (até a face de fora da outra parede).</summary>
        public Bordas Passa { get; set; }

        /// <summary>Ponta onde as peças de ajuste (34, bolachas) devem ficar; nenhuma = no início do trecho.</summary>
        public Bordas Ajuste { get; set; }

        /// <summary>Pontas que encostam no bloco que passa num canto (L): a bolacha evita ficar ali.</summary>
        public Bordas Canto { get; set; }

        public static Pontas Nenhuma => new Pontas();

        internal Pontas Espelhado() => new Pontas
        {
            SemCompensador = Espelhar(SemCompensador),
            Porta = Espelhar(Porta),
            Encontro = Espelhar(Encontro),
            Passa = Espelhar(Passa),
            Ajuste = Espelhar(Ajuste),
            Canto = Espelhar(Canto)
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
    /// Critérios, nesta ordem: 1) menos peças especiais (custo do catálogo: 39 → 34 → 19 → bolachas);
    /// 2) na 2ª fiada, juntas o mais perto possível de meio bloco das juntas da 1ª; 3) peças de ajuste
    /// encostadas na ponta preferida. As peças ficam a 1 cm uma da outra.
    /// </summary>
    public sealed class Modulador
    {
        private const int Inviavel = int.MaxValue / 4;
        private const int PesoPeca = 1000;   // custo do catálogo × 1000: a escolha das peças vem primeiro
        private const int PesoJunta = 20;    // por cm abaixo da defasagem ideal (2ª fiada)
        private const int PesoPosicao = 1;   // por passo de distância da peça de ajuste até a ponta preferida
        private const int PesoBolachaNoCanto = 300; // bolacha encostada no bloco do canto (o engenheiro evita)

        private readonly Familia _familia;
        private readonly OpcoesModulacao _opcoes;
        private readonly List<Peca> _pecas;
        private readonly int[] _unidades;

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
        }

        // ------------------------------------------------------------------ API

        /// <summary>Modula um trecho da 1ª fiada.</summary>
        /// <param name="comprimentoCm">Comprimento do trecho de alvenaria (cm).</param>
        /// <param name="pontas">O que há em cada ponta (porta, encontro…). Nulo = duas pontas soltas.</param>
        public ResultadoModulacao PrimeiraFiada(double comprimentoCm, Pontas pontas = null) =>
            Modular(comprimentoCm, null, pontas ?? Soltas(), "1ª fiada");

        /// <summary>Modula um trecho da 2ª fiada evitando junta a prumo com a 1ª.</summary>
        /// <param name="juntasPrimeiraFiada">Centro das juntas da 1ª fiada, em cm a partir do início do trecho.</param>
        public ResultadoModulacao SegundaFiada(double comprimentoCm, IEnumerable<double> juntasPrimeiraFiada, Pontas pontas = null) =>
            Modular(comprimentoCm, (juntasPrimeiraFiada ?? Enumerable.Empty<double>()).ToList(), pontas ?? Soltas(), "2ª fiada");

        /// <summary>
        /// Outra ordem para as mesmas peças de um trecho que fechou: o planejador do pavimento troca os 34 de
        /// ponta para a 2ª fiada ficar com juntas a meio bloco. Nulo se a sequência não vale para estas pontas.
        /// </summary>
        public ResultadoModulacao Reordenar(ResultadoModulacao original, IList<Peca> sequencia, Pontas pontas)
        {
            if (original == null || !original.Sucesso || sequencia.Count != original.Pecas.Count) return null;
            pontas = pontas ?? Soltas();
            var ultima = sequencia[sequencia.Count - 1];
            bool BolachaNoQuadrado(Peca p, Bordas lado) => p.Funcao == FuncaoPeca.Compensador && pontas.Passa.HasFlag(lado);
            if (sequencia.Count > 1 && (BolachaNoQuadrado(sequencia[0], Bordas.Inicio) || BolachaNoQuadrado(ultima, Bordas.Fim)))
                return null;

            int n = (int)Math.Round((original.ComprimentoMedido + _familia.Junta) / Passo);
            double modulavel = n * Passo - _familia.Junta;
            var r = new ResultadoModulacao
            {
                Sucesso = true,
                ComprimentoMedido = original.ComprimentoMedido,
                Avisos = original.Avisos.ToList(),
                Mensagem = original.Mensagem
            };
            PosicionarAjustado(r, sequencia.ToList(), original.ComprimentoMedido - modulavel, pontas);
            return r;
        }

        /// <summary>
        /// Custo (escala interna) do melhor preenchimento exato do trecho, ou nulo se ele não fecha com as
        /// regras. Usado pelo planejador para escolher a parede que passa em cada encontro e a posição das portas.
        /// </summary>
        public int? Custo(double comprimentoCm, Pontas pontas)
        {
            pontas = pontas ?? Soltas();
            int n = (int)Math.Round((comprimentoCm + _familia.Junta) / Passo);
            double modulavel = n * Passo - _familia.Junta;
            if (n <= 0 || Math.Abs(modulavel - comprimentoCm) > _opcoes.Tolerancia) return null;
            foreach (var t in Tentativas(false))
            {
                // Sem a preferência de posição: ela só ordena as peças dentro do trecho, não decide encontros.
                var r = Resolver(n, null, pontas, t, comPosicao: false);
                if (r != null) return r.Value.custo + t.Penalidade;
            }
            return null;
        }

        /// <summary>Comprimentos que fecham, o mais próximo abaixo e acima do medido.</summary>
        public List<int> ComprimentosValidosProximos(double comprimentoCm, Pontas pontas = null)
        {
            pontas = pontas ?? Soltas();
            var resultado = new List<int>();
            int n0 = (int)Math.Floor((comprimentoCm + _familia.Junta) / Passo);

            for (int n = n0; n > 0 && n > n0 - 40; n--)
                if (Fecha(n, pontas)) { resultado.Add(n * Passo - _familia.Junta); break; }
            for (int n = n0 + 1; n < n0 + 40; n++)
                if (Fecha(n, pontas)) { resultado.Add(n * Passo - _familia.Junta); break; }

            return resultado.Where(c => Math.Abs(c - comprimentoCm) > 1e-9).ToList();
        }

        private bool Fecha(int n, Pontas pontas) => Tentativas(false).Any(t => Resolver(n, null, pontas, t) != null);

        private static Pontas Soltas() => new Pontas { Porta = Bordas.Ambas, SemCompensador = Bordas.Ambas };

        // ------------------------------------------------------------------ modulação

        private sealed class Tentativa
        {
            public bool BolachaLivre;   // bolacha em qualquer posição (menos no quadrado do encontro)
            public double Minimo;       // defasagem mínima entre fiadas
            public string Aviso;
            public int Penalidade;      // para o planejador preferir soluções sem relaxar regra
        }

        /// <summary>Do mais rigoroso ao mais relaxado; a primeira que fecha vence e o que foi relaxado vira aviso.</summary>
        private List<Tentativa> Tentativas(bool segundaFiada)
        {
            double min = _opcoes.SobreposicaoMinima;
            var lista = new List<Tentativa>
            {
                new Tentativa { Minimo = min },
                new Tentativa
                {
                    BolachaLivre = true, Minimo = min, Penalidade = 200 * PesoPeca,
                    Aviso = "foi preciso bolacha longe dos encontros (o comprimento não fecha de outro jeito)"
                }
            };
            if (segundaFiada)
            {
                if (min > 5)
                    lista.Add(new Tentativa
                    {
                        BolachaLivre = true, Minimo = 5,
                        Aviso = $"defasagem entre as fiadas reduzida para 5 cm (o mínimo é {Cm(min)} cm)"
                    });
                lista.Add(new Tentativa { BolachaLivre = true, Minimo = 0, Aviso = "junta a prumo inevitável neste trecho" });
            }
            return lista;
        }

        private ResultadoModulacao Modular(double comprimentoCm, List<double> juntas1, Pontas pontas, string nomeFiada)
        {
            var r = new ResultadoModulacao { ComprimentoMedido = comprimentoCm };
            string medido = Cm(comprimentoCm);
            bool segunda = juntas1 != null;
            var tentativas = Tentativas(segunda);

            int n = (int)Math.Round((comprimentoCm + _familia.Junta) / Passo);
            double modulavel = n * Passo - _familia.Junta;
            if (n > 0 && Math.Abs(modulavel - comprimentoCm) <= _opcoes.Tolerancia)
            {
                foreach (var t in tentativas)
                {
                    var res = Resolver(n, juntas1, pontas, t);
                    if (res == null) continue;
                    var seq = res.Value.seq;
                    PosicionarAjustado(r, seq, comprimentoCm - modulavel, pontas);
                    r.Sucesso = true;
                    if (t.Aviso != null) r.Avisos.Add(t.Aviso);
                    r.Mensagem = $"{nomeFiada}: {medido} cm = " + string.Join(" + ", seq.Select(x => x.Codigo)) +
                                 (r.Avisos.Count > 0 ? " (" + string.Join("; ", r.Avisos) + ")" : "");
                    return r;
                }
            }

            // Não fecha: coloca as peças ("a parede chegando") ancoradas na ponta que encosta em outra parede,
            // para os blocos de paredes diferentes ficarem sempre a 1 cm; a sobra vai para o lado da porta/solto.
            r.Sugestoes = ComprimentosValidosProximos(comprimentoCm, pontas);
            bool ancoraNoFim = pontas.Dura(Bordas.Fim) && !pontas.Dura(Bordas.Inicio);
            var p2 = ancoraNoFim ? pontas.Espelhado() : pontas;
            // Na ponta da sobra a peça não encosta em nada: vale como ponta solta.
            p2 = new Pontas
            {
                SemCompensador = p2.SemCompensador | Bordas.Fim,
                Porta = p2.Porta | Bordas.Fim,
                Encontro = p2.Encontro & ~Bordas.Fim,
                Passa = p2.Passa & ~Bordas.Fim,
                Ajuste = p2.Ajuste & ~Bordas.Fim
            };
            List<double> j2 = segunda && ancoraNoFim ? juntas1.Select(j => comprimentoCm - j).ToList() : juntas1;

            // Só o que cabe sem invadir a outra parede (sobra >= 0).
            int nMax = (int)Math.Floor((comprimentoCm + _familia.Junta + 1e-6) / Passo);
            for (int k = nMax; k > 0; k--)
            {
                foreach (var t in tentativas)
                {
                    var res = Resolver(k, j2, p2, t);
                    if (res == null) continue;
                    var seq = res.Value.seq;

                    double ocupado = k * Passo - _familia.Junta;
                    r.Sobra = comprimentoCm - ocupado;
                    r.SobraNoInicio = ancoraNoFim;
                    if (ancoraNoFim) seq.Reverse();
                    Posicionar(r, seq, ancoraNoFim ? r.Sobra : 0, 0);
                    if (t.Aviso != null) r.Avisos.Add(t.Aviso);
                    string lado = ancoraNoFim ? "no início (junto à porta/ponta solta)"
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
        /// comprimento medido e o modulado vai para a ponta "mole" (porta/solta); se as duas pontas encostam em
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
        ///  - 19: só na ombreira de porta ou ponta solta; onde a parede continua, só se o trecho inteiro for o
        ///    19 (boneca entre a porta e o encontro);
        ///  - bolacha (4 e 9): só encostada num encontro em que a parede não passa (ao lado do 54, do bloco
        ///    que atravessa ou da parede que passa), ou quando o trecho inteiro é a bolacha; nunca no meio;
        ///  - 34 e bolachas puxados para a ponta de ajuste;
        ///  - 2ª fiada: junta a menos da defasagem mínima é proibida e abaixo da ideal é penalizada.
        /// </summary>
        private (List<Peca> seq, int custo)? Resolver(int n, List<double> juntas1, Pontas p, Tentativa t, bool comPosicao = true)
        {
            var melhor = new int[n + 1];
            var escolha = new int[n + 1];
            for (int i = 0; i < n; i++) { melhor[i] = Inviavel; escolha[i] = -1; }
            melhor[n] = 0;

            bool ajusteIni = p.Ajuste.HasFlag(Bordas.Inicio), ajusteFim = p.Ajuste.HasFlag(Bordas.Fim);

            for (int i = n - 1; i >= 0; i--)
            {
                for (int k = 0; k < _pecas.Count; k++)
                {
                    int fim = i + _unidades[k];
                    if (fim > n || melhor[fim] >= Inviavel) continue;

                    bool ini = i == 0, ultimo = fim == n, unica = ini && ultimo;
                    var peca = _pecas[k];
                    int custo = peca.Custo * PesoPeca;

                    if (peca.Funcao == FuncaoPeca.Meio)
                    {
                        bool naPorta = (ini && p.Porta.HasFlag(Bordas.Inicio)) || (ultimo && p.Porta.HasFlag(Bordas.Fim));
                        bool noEncontro = (ini && p.Encontro.HasFlag(Bordas.Inicio)) || (ultimo && p.Encontro.HasFlag(Bordas.Fim));
                        if (!naPorta) continue;                  // nunca no meio nem só entre encontros
                        if (noEncontro && !unica) continue;      // onde a parede continua, só a boneca inteira
                    }
                    else if (peca.Funcao == FuncaoPeca.Compensador && !unica)
                    {
                        bool quadrado = (ini && p.Passa.HasFlag(Bordas.Inicio)) || (ultimo && p.Passa.HasFlag(Bordas.Fim));
                        if (quadrado) continue;                  // no quadrado do encontro nunca (graute)
                        if (!t.BolachaLivre)
                        {
                            bool encostaIni = ini && p.Encontro.HasFlag(Bordas.Inicio) && !p.SemCompensador.HasFlag(Bordas.Inicio);
                            bool encostaFim = ultimo && p.Encontro.HasFlag(Bordas.Fim) && !p.SemCompensador.HasFlag(Bordas.Fim);
                            if (!encostaIni && !encostaFim) continue;
                        }
                        if ((ini && p.Canto.HasFlag(Bordas.Inicio)) || (ultimo && p.Canto.HasFlag(Bordas.Fim)))
                            custo += PesoBolachaNoCanto;
                    }

                    // Peças de ajuste encostadas na ponta preferida (o engenheiro agrupa os 34 e as bolachas no encontro).
                    if (comPosicao && (peca.Funcao == FuncaoPeca.Compensador || peca.Funcao == FuncaoPeca.Amarracao))
                    {
                        int dIni = i, dFim = n - fim;
                        int d = ajusteIni && ajusteFim ? Math.Min(dIni, dFim) : ajusteFim ? dFim : dIni;
                        custo += d * PesoPosicao;
                    }

                    int custoJunta = 0;
                    if (!ultimo)
                    {
                        custoJunta = CustoJunta(fim, juntas1, t.Minimo);
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
            return (seq, melhor[0]);
        }

        private int CustoJunta(int fronteira, List<double> juntas1, double minimo)
        {
            if (juntas1 == null || juntas1.Count == 0) return 0;
            double centro = fronteira * Passo - _familia.Junta / 2.0;
            double d = juntas1.Min(j => Math.Abs(j - centro));
            if (d < minimo - 1e-9) return Inviavel;
            return d >= _opcoes.SobreposicaoIdeal ? 0 : (int)Math.Round((_opcoes.SobreposicaoIdeal - d) * PesoJunta);
        }

        private static string Cm(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        private static string SufixoSugestoes(List<int> sugestoes) =>
            sugestoes.Count == 0 ? "." : $". Comprimentos que fecham mais próximos: {string.Join(" ou ", sugestoes)} cm.";

        private static int Mdc(int a, int b) => b == 0 ? a : Mdc(b, a % b);
    }
}
