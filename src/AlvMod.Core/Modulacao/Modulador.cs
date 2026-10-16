using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AlvMod.Core.Catalogo;

namespace AlvMod.Core.Modulacao
{
    /// <summary>Extremidades do trecho que encostam numa abertura (porta/janela).</summary>
    [Flags]
    public enum Bordas
    {
        Nenhuma = 0,
        Inicio = 1,
        Fim = 2,
        Ambas = Inicio | Fim
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

        /// <summary>Não fechou, mas as peças foram colocadas a partir do início e sobrou <see cref="Sobra"/>.</summary>
        public bool Parcial => !Sucesso && Pecas.Count > 0;

        public string Mensagem { get; set; }
        public double ComprimentoMedido { get; set; }

        /// <summary>Comprimento (cm) não preenchido no fim do trecho quando o resultado é parcial.</summary>
        public double Sobra { get; set; }

        public List<PecaPosicionada> Pecas { get; set; } = new List<PecaPosicionada>();
        public List<int> Sugestoes { get; set; } = new List<int>();

        /// <summary>Posições (cm) do centro de cada junta interna.</summary>
        public IEnumerable<double> Juntas(int junta) =>
            Pecas.Take(Math.Max(0, Pecas.Count - 1)).Select(p => p.Fim + junta / 2.0);
    }

    /// <summary>
    /// Modulação de um trecho reto de parede por programação dinâmica.
    /// Trabalha numa grade inteira cujo passo é o MDC de (peça + junta) de todas as peças
    /// (5 cm na família 39), para evitar erro de ponto flutuante.
    /// </summary>
    public sealed class Modulador
    {
        private const int Inviavel = int.MaxValue / 4;

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
        }

        /// <param name="aberturas">Pontas que encostam em vão (ou encontro): sem compensador ali.</param>
        /// <param name="encontros">Pontas que chegam num encontro de paredes: só nelas entra peça de encontro (54).</param>
        public ResultadoModulacao PrimeiraFiada(double comprimentoCm, Bordas aberturas = Bordas.Nenhuma,
            Bordas encontros = Bordas.Nenhuma) =>
            Modular(comprimentoCm, null, aberturas, encontros, "1ª fiada");

        /// <param name="juntasPrimeiraFiada">Centro das juntas da 1ª fiada, em cm a partir do início do trecho.</param>
        public ResultadoModulacao SegundaFiada(double comprimentoCm, IEnumerable<double> juntasPrimeiraFiada,
            Bordas aberturas = Bordas.Nenhuma, Bordas encontros = Bordas.Nenhuma) =>
            Modular(comprimentoCm, (juntasPrimeiraFiada ?? Enumerable.Empty<double>()).ToList(), aberturas, encontros, "2ª fiada");

        /// <summary>Comprimentos que fecham, o mais próximo abaixo e acima do medido.</summary>
        public List<int> ComprimentosValidosProximos(double comprimentoCm, Bordas aberturas = Bordas.Nenhuma,
            Bordas encontros = Bordas.Nenhuma)
        {
            var resultado = new List<int>();
            int? abaixo = MaiorQueFecha((int)Math.Floor((comprimentoCm + _familia.Junta) / Passo), aberturas, encontros);
            if (abaixo.HasValue) resultado.Add(abaixo.Value * Passo - _familia.Junta);

            int n0 = (int)Math.Floor((comprimentoCm + _familia.Junta) / Passo);
            for (int n = n0 + 1; n < n0 + 40; n++)
                if (Resolver(n, null, aberturas, encontros) != null) { resultado.Add(n * Passo - _familia.Junta); break; }

            return resultado.Where(c => Math.Abs(c - comprimentoCm) > 1e-9).ToList();
        }

        private ResultadoModulacao Modular(double comprimentoCm, List<double> juntas1, Bordas aberturas, Bordas encontros,
            string nomeFiada)
        {
            var r = new ResultadoModulacao { ComprimentoMedido = comprimentoCm };
            string medido = Cm(comprimentoCm);

            int n = (int)Math.Round((comprimentoCm + _familia.Junta) / Passo);
            double modulavel = n * Passo - _familia.Junta;
            List<Peca> sequencia = null;
            if (n > 0 && Math.Abs(modulavel - comprimentoCm) <= _opcoes.Tolerancia)
                sequencia = Resolver(n, juntas1, aberturas, encontros);

            if (sequencia != null)
            {
                Posicionar(r, sequencia);
                r.Sucesso = true;
                r.Mensagem = $"{nomeFiada}: {medido} cm = " + string.Join(" + ", sequencia.Select(p => p.Codigo));
                return r;
            }

            bool fechaSemJuntas = juntas1 != null && n > 0 && Math.Abs(modulavel - comprimentoCm) <= _opcoes.Tolerancia
                                  && Resolver(n, null, aberturas, encontros) != null;
            if (fechaSemJuntas)
            {
                // O comprimento fecha; o que impede é a 1ª fiada. Não força junta a prumo.
                r.Mensagem = $"não existe {nomeFiada} de {medido} cm sem junta a prumo " +
                             $"(defasagem mínima {Cm(_opcoes.SobreposicaoMinima)} cm). Ajuste a 1ª fiada manualmente.";
                return r;
            }

            // Não fecha: coloca as peças a partir do início ("a parede chegando") e deixa a sobra no fim.
            r.Sugestoes = ComprimentosValidosProximos(comprimentoCm, aberturas, encontros);
            var parcial = PreencherParcial(comprimentoCm, juntas1, aberturas & Bordas.Inicio, encontros & Bordas.Inicio);
            if (parcial != null)
            {
                Posicionar(r, parcial);
                r.Sobra = comprimentoCm - r.Pecas.Last().Fim;
            }
            r.Mensagem = $"{medido} cm não fecha com as peças habilitadas da família {_familia.Nome}" +
                         (r.Parcial ? $"; peças colocadas a partir do início, sobra de {Cm(r.Sobra)} cm no fim para ajuste manual" : "") +
                         SufixoSugestoes(r.Sugestoes);
            return r;
        }

        /// <summary>Maior preenchimento que cabe no comprimento, começando do início do trecho.</summary>
        private List<Peca> PreencherParcial(double comprimentoCm, List<double> juntas1, Bordas aberturas, Bordas encontros)
        {
            int nMax = (int)Math.Floor((comprimentoCm + _familia.Junta + _opcoes.Tolerancia) / Passo);
            for (int n = nMax; n > 0; n--)
            {
                var seq = Resolver(n, juntas1, aberturas, encontros);
                if (seq != null) return seq;
            }
            return null;
        }

        private int? MaiorQueFecha(int nMax, Bordas aberturas, Bordas encontros)
        {
            for (int n = nMax; n > 0 && n > nMax - 40; n--)
                if (Resolver(n, null, aberturas, encontros) != null) return n;
            return null;
        }

        private void Posicionar(ResultadoModulacao r, List<Peca> sequencia)
        {
            double x = 0;
            foreach (var peca in sequencia)
            {
                r.Pecas.Add(new PecaPosicionada { Peca = peca, Inicio = x });
                x += peca.Comprimento + _familia.Junta;
            }
        }

        /// <summary>
        /// DP de trás para frente: melhor[i] = menor custo para preencher da unidade i até n.
        /// Como o desempate favorece a peça mais barata na posição mais à esquerda, os blocos
        /// principais ficam no começo e as peças especiais no fim do trecho.
        /// Regras: compensador nunca encosta em abertura (questionário, item 34); bloco de encontro (54) só
        /// na ponta que chega num encontro de paredes; na 2ª fiada,
        /// junta a menos da defasagem mínima é proibida e abaixo da ideal é penalizada.
        /// </summary>
        private List<Peca> Resolver(int n, List<double> juntas1, Bordas aberturas, Bordas encontros)
        {
            var melhor = new int[n + 1];
            var escolha = new int[n + 1];
            for (int i = 0; i < n; i++) { melhor[i] = Inviavel; escolha[i] = -1; }
            melhor[n] = 0;

            for (int i = n - 1; i >= 0; i--)
            {
                for (int k = 0; k < _pecas.Count; k++)
                {
                    int fim = i + _unidades[k];
                    if (fim > n || melhor[fim] >= Inviavel) continue;

                    if (_pecas[k].EhCompensador &&
                        ((i == 0 && aberturas.HasFlag(Bordas.Inicio)) || (fim == n && aberturas.HasFlag(Bordas.Fim))))
                        continue;

                    // Peça de encontro (54) só na ponta que chega num encontro de paredes.
                    if (_pecas[k].Funcao == FuncaoPeca.Encontro &&
                        !((i == 0 && encontros.HasFlag(Bordas.Inicio)) || (fim == n && encontros.HasFlag(Bordas.Fim))))
                        continue;

                    int custoJunta = 0;
                    if (fim < n)
                    {
                        custoJunta = CustoJunta(fim, juntas1);
                        if (custoJunta >= Inviavel) continue;
                    }

                    int custo = _pecas[k].Custo + custoJunta + melhor[fim];
                    if (custo < melhor[i]) { melhor[i] = custo; escolha[i] = k; }
                }
            }

            if (melhor[0] >= Inviavel) return null;

            var seq = new List<Peca>();
            for (int i = 0; i < n; i += _unidades[escolha[i]])
                seq.Add(_pecas[escolha[i]]);
            return seq;
        }

        private int CustoJunta(int fronteira, List<double> juntas1)
        {
            if (juntas1 == null || juntas1.Count == 0) return 0;
            double centro = fronteira * Passo - _familia.Junta / 2.0;
            double d = juntas1.Min(j => Math.Abs(j - centro));
            if (d < _opcoes.SobreposicaoMinima - 1e-9) return Inviavel;
            return d >= _opcoes.SobreposicaoIdeal ? 0 : (int)Math.Round((_opcoes.SobreposicaoIdeal - d) * _opcoes.PenalidadePorCm);
        }

        private static string Cm(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        private static string SufixoSugestoes(List<int> sugestoes) =>
            sugestoes.Count == 0 ? "." : $". Comprimentos que fecham mais próximos: {string.Join(" ou ", sugestoes)} cm.";

        private static int Mdc(int a, int b) => b == 0 ? a : Mdc(b, a % b);
    }
}
