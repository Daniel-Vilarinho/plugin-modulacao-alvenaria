using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AlvMod.Core.Catalogo;

namespace AlvMod.Core.Modulacao
{
    public sealed class OpcoesModulacao
    {
        /// <summary>Diferença máxima (cm) aceita entre o comprimento medido e um comprimento modulável.</summary>
        public double Tolerancia { get; set; } = 0.5;

        /// <summary>Distância mínima (cm) entre uma junta da 2ª fiada e qualquer junta da 1ª.</summary>
        public double SobreposicaoMinima { get; set; } = 5;
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
        public bool Sucesso { get; set; }
        public string Mensagem { get; set; }
        public double ComprimentoMedido { get; set; }
        public List<PecaPosicionada> Pecas { get; set; } = new List<PecaPosicionada>();
        public List<int> Sugestoes { get; set; } = new List<int>();

        /// <summary>Posições (cm) do centro de cada junta interna.</summary>
        public IEnumerable<double> Juntas(int junta) =>
            Pecas.Take(Pecas.Count - 1).Select(p => p.Fim + junta / 2.0);
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

        public Modulador(Familia familia, OpcoesModulacao opcoes = null)
        {
            _familia = familia ?? throw new ArgumentNullException(nameof(familia));
            _opcoes = opcoes ?? new OpcoesModulacao();

            // Ordem = desempate: mais barata primeiro; entre iguais, a maior.
            _pecas = familia.PecasDeFiada()
                .OrderBy(p => p.Custo)
                .ThenByDescending(p => p.Comprimento)
                .ToList();
            if (_pecas.Count == 0)
                throw new InvalidOperationException("A família não tem peças habilitadas.");

            Passo = _pecas.Select(p => p.Comprimento + familia.Junta).Aggregate(Mdc);
            _unidades = _pecas.Select(p => (p.Comprimento + familia.Junta) / Passo).ToArray();
        }

        public ResultadoModulacao PrimeiraFiada(double comprimentoCm) =>
            Modular(comprimentoCm, null, "1ª fiada");

        /// <param name="juntasPrimeiraFiada">Centro das juntas da 1ª fiada, em cm a partir do início do trecho.</param>
        public ResultadoModulacao SegundaFiada(double comprimentoCm, IEnumerable<double> juntasPrimeiraFiada) =>
            Modular(comprimentoCm, (juntasPrimeiraFiada ?? Enumerable.Empty<double>()).ToList(), "2ª fiada");

        /// <summary>Comprimentos que fecham, o mais próximo abaixo e acima do medido.</summary>
        public List<int> ComprimentosValidosProximos(double comprimentoCm)
        {
            var resultado = new List<int>();
            int n0 = (int)Math.Floor((comprimentoCm + _familia.Junta) / Passo);

            for (int n = n0; n > 0 && n > n0 - 40; n--)
                if (Resolver(n, null) != null) { resultado.Add(n * Passo - _familia.Junta); break; }

            for (int n = n0 + 1; n < n0 + 40; n++)
                if (Resolver(n, null) != null) { resultado.Add(n * Passo - _familia.Junta); break; }

            return resultado.Where(c => Math.Abs(c - comprimentoCm) > 1e-9).ToList();
        }

        private ResultadoModulacao Modular(double comprimentoCm, List<double> juntasProibidas, string nomeFiada)
        {
            var r = new ResultadoModulacao { ComprimentoMedido = comprimentoCm };
            string medido = comprimentoCm.ToString("0.0", CultureInfo.InvariantCulture);

            int n = (int)Math.Round((comprimentoCm + _familia.Junta) / Passo);
            double modulavel = n * Passo - _familia.Junta;
            if (n <= 0 || Math.Abs(modulavel - comprimentoCm) > _opcoes.Tolerancia)
            {
                r.Sugestoes = ComprimentosValidosProximos(comprimentoCm);
                r.Mensagem = $"{medido} cm não fecha com as peças da família {_familia.Nome}" +
                             SufixoSugestoes(r.Sugestoes);
                return r;
            }

            var sequencia = Resolver(n, juntasProibidas);
            if (sequencia == null)
            {
                r.Sugestoes = juntasProibidas == null ? ComprimentosValidosProximos(comprimentoCm) : new List<int>();
                r.Mensagem = juntasProibidas == null
                    ? $"{medido} cm não fecha com as peças habilitadas da família {_familia.Nome}" + SufixoSugestoes(r.Sugestoes)
                    : $"não existe {nomeFiada} de {medido} cm sem junta a prumo " +
                      $"(sobreposição mínima {_opcoes.SobreposicaoMinima} cm). Ajuste a 1ª fiada manualmente.";
                return r;
            }

            double x = 0;
            foreach (var peca in sequencia)
            {
                r.Pecas.Add(new PecaPosicionada { Peca = peca, Inicio = x });
                x += peca.Comprimento + _familia.Junta;
            }
            r.Sucesso = true;
            r.Mensagem = $"{nomeFiada}: {medido} cm = " + string.Join(" + ", sequencia.Select(p => p.Codigo));
            return r;
        }

        /// <summary>
        /// DP de trás para frente: melhor[i] = menor custo para preencher da unidade i até n.
        /// Como o desempate favorece a peça mais barata na posição mais à esquerda, os blocos
        /// principais ficam no começo e as peças especiais no fim do trecho.
        /// </summary>
        private List<Peca> Resolver(int n, List<double> juntasProibidas)
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
                    if (fim < n && !JuntaPermitida(fim, juntasProibidas)) continue;

                    int custo = _pecas[k].Custo + melhor[fim];
                    if (custo < melhor[i]) { melhor[i] = custo; escolha[i] = k; }
                }
            }

            if (melhor[0] >= Inviavel) return null;

            var seq = new List<Peca>();
            for (int i = 0; i < n; i += _unidades[escolha[i]])
                seq.Add(_pecas[escolha[i]]);
            return seq;
        }

        private bool JuntaPermitida(int fronteira, List<double> juntasProibidas)
        {
            if (juntasProibidas == null) return true;
            double centro = fronteira * Passo - _familia.Junta / 2.0;
            return juntasProibidas.All(j => Math.Abs(j - centro) >= _opcoes.SobreposicaoMinima);
        }

        private static string SufixoSugestoes(List<int> sugestoes) =>
            sugestoes.Count == 0 ? "." : $". Comprimentos válidos mais próximos: {string.Join(" ou ", sugestoes)} cm.";

        private static int Mdc(int a, int b) => b == 0 ? a : Mdc(b, a % b);
    }
}
