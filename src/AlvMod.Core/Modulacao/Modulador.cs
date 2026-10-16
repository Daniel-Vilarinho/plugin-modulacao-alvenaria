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
    /// (questionário e correções do engenheiro):
    ///  - nunca bolacha (4 e 9) em ponta de porta, encontro ou canto (precisa de septo para o graute);
    ///  - ponta que encosta em porta termina em meio bloco (19);
    ///  - o 54 só entra na ponta de parede que atravessa um encontro em T;
    ///  - o 34 é recomendado nos cantos.
    /// </summary>
    public sealed class Pontas
    {
        /// <summary>Pontas em porta, encontro ou canto: sem bolacha.</summary>
        public Bordas SemCompensador { get; set; }

        /// <summary>Pontas que encostam em porta/passagem: a última peça é o meio bloco (19).</summary>
        public Bordas Porta { get; set; }

        /// <summary>Pontas de parede que atravessam um encontro em T: única posição do 54.</summary>
        public Bordas EncontroT { get; set; }

        /// <summary>Pontas em canto (encontro em L): posição recomendada do 34.</summary>
        public Bordas Canto { get; set; }

        public static Pontas Nenhuma => new Pontas();

        internal Pontas Espelhado() => new Pontas
        {
            SemCompensador = Espelhar(SemCompensador),
            Porta = Espelhar(Porta),
            EncontroT = Espelhar(EncontroT),
            Canto = Espelhar(Canto)
        };

        internal Pontas SemMarcasDoLado(Bordas lado) => new Pontas
        {
            SemCompensador = SemCompensador,
            Porta = Porta,
            EncontroT = EncontroT & ~lado,
            Canto = Canto & ~lado
        };

        internal static Bordas Espelhar(Bordas b)
        {
            var r = Bordas.Nenhuma;
            if (b.HasFlag(Bordas.Inicio)) r |= Bordas.Fim;
            if (b.HasFlag(Bordas.Fim)) r |= Bordas.Inicio;
            return r;
        }

        /// <summary>Ponta "dura": encosta em outra parede (encontro/canto), onde a sobra não pode ficar.</summary>
        public bool Dura(Bordas lado) => SemCompensador.HasFlag(lado) && !Porta.HasFlag(lado);
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
        /// <summary>O trecho fechou exatamente, sem sobra.</summary>
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

        /// <summary>Posições (cm) do centro de cada junta interna.</summary>
        public IEnumerable<double> Juntas(int junta) =>
            Pecas.Take(Math.Max(0, Pecas.Count - 1)).Select(p => p.Fim + junta / 2.0);
    }

    /// <summary>
    /// Modulação de um trecho reto de parede por programação dinâmica.
    /// Trabalha numa grade inteira cujo passo é o MDC de (peça + junta) de todas as peças
    /// (5 cm na família 39), para evitar erro de ponto flutuante.
    ///
    /// Prioridade (custo): 39 → 34 → 19/54 (só onde permitido) → bolachas. Todas as peças ficam a 1 cm
    /// (a junta) uma da outra; quando o trecho não fecha, a sobra vai para o lado da porta.
    /// </summary>
    public sealed class Modulador
    {
        private const int Inviavel = int.MaxValue / 4;

        private readonly Familia _familia;
        private readonly OpcoesModulacao _opcoes;
        private readonly List<Peca> _pecas;
        private readonly int[] _unidades;
        private readonly int _iMeio;
        private readonly int _custoPrincipal;

        public int Passo { get; }
        public Familia Familia => _familia;

        public Modulador(Familia familia, OpcoesModulacao opcoes = null)
        {
            _familia = familia ?? throw new ArgumentNullException(nameof(familia));
            _opcoes = opcoes ?? new OpcoesModulacao();

            // Ordem = desempate: mais barata primeiro; entre iguais, a maior.
            _pecas = familia.PecasDeFiada()
                .Where(p => p.Comprimento > 0)
                .OrderBy(p => p.Custo)
                .ThenByDescending(p => p.Comprimento)
                .ToList();
            if (_pecas.Count == 0)
                throw new InvalidOperationException($"A família {familia.Nome} não tem peças habilitadas.");

            Passo = _pecas.Select(p => p.Comprimento + familia.Junta).Aggregate(Mdc);
            _unidades = _pecas.Select(p => (p.Comprimento + familia.Junta) / Passo).ToArray();
            _iMeio = _pecas.FindIndex(p => p.Funcao == FuncaoPeca.Meio);
            var principais = _pecas.Where(p => p.Funcao == FuncaoPeca.Principal).Select(p => p.Custo).ToList();
            _custoPrincipal = principais.Count > 0 ? principais.Min() : _pecas.Min(p => p.Custo);
        }

        // ------------------------------------------------------------------ API

        public ResultadoModulacao PrimeiraFiada(double comprimentoCm, Pontas pontas) =>
            Modular(comprimentoCm, null, pontas ?? Pontas.Nenhuma, "1ª fiada");

        public ResultadoModulacao PrimeiraFiada(double comprimentoCm, Bordas aberturas = Bordas.Nenhuma,
            Bordas encontros = Bordas.Nenhuma) =>
            PrimeiraFiada(comprimentoCm, Converter(aberturas, encontros));

        /// <param name="juntasPrimeiraFiada">Centro das juntas da 1ª fiada, em cm a partir do início do trecho.</param>
        public ResultadoModulacao SegundaFiada(double comprimentoCm, IEnumerable<double> juntasPrimeiraFiada, Pontas pontas) =>
            Modular(comprimentoCm, (juntasPrimeiraFiada ?? Enumerable.Empty<double>()).ToList(), pontas ?? Pontas.Nenhuma, "2ª fiada");

        public ResultadoModulacao SegundaFiada(double comprimentoCm, IEnumerable<double> juntasPrimeiraFiada,
            Bordas aberturas = Bordas.Nenhuma, Bordas encontros = Bordas.Nenhuma) =>
            SegundaFiada(comprimentoCm, juntasPrimeiraFiada, Converter(aberturas, encontros));

        private static Pontas Converter(Bordas aberturas, Bordas encontros) =>
            new Pontas { SemCompensador = aberturas | encontros, EncontroT = encontros };

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
            Resolver(n, null, pontas, 0, true) != null || Resolver(n, null, pontas, 0, false) != null;

        // ------------------------------------------------------------------ modulação

        private sealed class Tentativa
        {
            public bool Porta19;
            public double Minimo;
            public string Aviso;
        }

        /// <summary>Do mais rigoroso ao mais relaxado; a primeira que fecha vence e o que foi relaxado vira aviso.</summary>
        private List<Tentativa> Tentativas(bool segundaFiada, Pontas p)
        {
            double min = _opcoes.SobreposicaoMinima;
            bool temPorta = (p.Porta != Bordas.Nenhuma) && _iMeio >= 0;
            var lista = new List<Tentativa> { new Tentativa { Porta19 = temPorta, Minimo = min } };
            if (temPorta)
                lista.Add(new Tentativa { Porta19 = false, Minimo = min, Aviso = "a ponta junto à porta não coube em meio bloco (19)" });
            if (segundaFiada)
            {
                if (min > 5)
                    lista.Add(new Tentativa
                    {
                        Porta19 = false,
                        Minimo = 5,
                        Aviso = $"defasagem entre as fiadas reduzida para 5 cm (o mínimo é {Cm(min)} cm)"
                    });
                lista.Add(new Tentativa { Porta19 = false, Minimo = 0, Aviso = "junta a prumo inevitável neste trecho" });
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
                    var seq = Resolver(n, juntas1, pontas, t.Minimo, t.Porta19);
                    if (seq == null) continue;
                    Posicionar(r, seq, 0);
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
            var p2 = ancoraNoFim ? pontas.Espelhado() : pontas;
            p2 = p2.SemMarcasDoLado(Bordas.Fim); // a ponta da sobra não encosta de verdade na outra parede
            List<double> j2 = segunda && ancoraNoFim ? juntas1.Select(j => comprimentoCm - j).ToList() : juntas1;

            int nMax = (int)Math.Floor((comprimentoCm + _familia.Junta + _opcoes.Tolerancia) / Passo);
            for (int k = nMax; k > 0; k--)
            {
                foreach (var t in tentativas)
                {
                    var seq = Resolver(k, j2, p2, t.Minimo, t.Porta19);
                    if (seq == null) continue;

                    double ocupado = k * Passo - _familia.Junta;
                    r.Sobra = comprimentoCm - ocupado;
                    r.SobraNoInicio = ancoraNoFim;
                    if (ancoraNoFim) seq.Reverse();
                    Posicionar(r, seq, ancoraNoFim ? r.Sobra : 0);
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

        private void Posicionar(ResultadoModulacao r, List<Peca> sequencia, double inicio)
        {
            double x = inicio;
            foreach (var peca in sequencia)
            {
                r.Pecas.Add(new PecaPosicionada { Peca = peca, Inicio = x });
                x += peca.Comprimento + _familia.Junta;
            }
        }

        // ------------------------------------------------------------------ programação dinâmica

        /// <summary>
        /// DP de trás para frente: melhor[i] = menor custo para preencher da unidade i até n.
        /// Regras de posição (correções do engenheiro):
        ///  - 54: só na ponta de parede que atravessa um T; custa como um 39;
        ///  - 19: só nas pontas, nunca no meio; na ponta que encosta em porta é obrigatório e custa como um 39;
        ///  - 34: segunda prioridade; em canto (L) custa menos que no meio da parede;
        ///  - bolacha (4 e 9): nunca em ponta de porta, encontro ou canto;
        ///  - 2ª fiada: junta a menos da defasagem mínima é proibida e abaixo da ideal é penalizada
        ///    (exceto a junta ao lado do 19 da porta, que é a mesma nas duas fiadas).
        /// </summary>
        private List<Peca> Resolver(int n, List<double> juntas1, Pontas p, double minimo, bool exigirPorta19)
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

                    switch (peca.Funcao)
                    {
                        case FuncaoPeca.Compensador:
                            if ((ini && p.SemCompensador.HasFlag(Bordas.Inicio)) ||
                                (ultimo && p.SemCompensador.HasFlag(Bordas.Fim)))
                                continue;
                            break;
                        case FuncaoPeca.Meio:
                            if (!ini && !ultimo) continue; // nunca no meio da parede
                            if ((ini && p.Porta.HasFlag(Bordas.Inicio)) || (ultimo && p.Porta.HasFlag(Bordas.Fim)))
                                custo = _custoPrincipal;
                            break;
                        case FuncaoPeca.Encontro:
                            if (!((ini && p.EncontroT.HasFlag(Bordas.Inicio)) || (ultimo && p.EncontroT.HasFlag(Bordas.Fim))))
                                continue; // o 54 só no encontro em T
                            custo = _custoPrincipal;
                            break;
                        case FuncaoPeca.Amarracao:
                            if ((ini && p.Canto.HasFlag(Bordas.Inicio)) || (ultimo && p.Canto.HasFlag(Bordas.Fim)))
                                custo = (peca.Custo + _custoPrincipal) / 2;
                            break;
                    }

                    if (exigirPorta19 &&
                        ((ini && p.Porta.HasFlag(Bordas.Inicio)) || (ultimo && p.Porta.HasFlag(Bordas.Fim))) &&
                        peca.Funcao != FuncaoPeca.Meio)
                        continue;

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
