using System;
using System.Collections.Generic;
using System.Linq;

namespace AlvMod.Core.Planta
{
    /// <summary>Segmento de reta lido do desenho, já convertido para cm.</summary>
    public sealed class Segmento
    {
        public string Layer { get; set; }
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }

        public Segmento() { }

        public Segmento(string layer, double x1, double y1, double x2, double y2)
        {
            Layer = layer; X1 = x1; Y1 = y1; X2 = x2; Y2 = y2;
        }

        public double Comprimento => Math.Sqrt((X2 - X1) * (X2 - X1) + (Y2 - Y1) * (Y2 - Y1));
    }

    /// <summary>Texto da planta (ex.: "P1", "J3"), em cm. Rótulos P*/J* ajudam a classificar os vãos.</summary>
    public sealed class Rotulo
    {
        public string Texto { get; set; }
        public double X { get; set; }
        public double Y { get; set; }

        public Rotulo() { }
        public Rotulo(string texto, double x, double y) { Texto = texto; X = x; Y = y; }
    }

    public enum Orientacao
    {
        Horizontal,
        Vertical
    }

    public enum TipoVao
    {
        Porta,
        Janela,
        Passagem
    }

    /// <summary>Vão na parede, em cm ao longo do eixo (coordenadas absolutas do desenho).</summary>
    public sealed class Vao
    {
        public TipoVao Tipo { get; set; }
        public double Inicio { get; set; }
        public double Fim { get; set; }
        public double Largura => Fim - Inicio;
    }

    /// <summary>
    /// Parede reta ortogonal, representada pelo eixo. Horizontal: y = Coord, x de Inicio a Fim.
    /// Vertical: x = Coord, y de Inicio a Fim. Sempre Inicio &lt; Fim (esquerda→direita, baixo→cima).
    /// </summary>
    public sealed class Parede
    {
        public int Indice { get; set; }
        public Orientacao Orientacao { get; set; }
        public double Coord { get; set; }
        public double Inicio { get; set; }
        public double Fim { get; set; }

        /// <summary>Espessura desenhada pelo arquiteto (com acabamento), em cm.</summary>
        public double Espessura { get; set; }

        public List<Vao> Vaos { get; set; } = new List<Vao>();

        /// <summary>Nome da numeração (ex.: "PAREDE 3").</summary>
        public string Nome { get; set; }

        public double Comprimento => Fim - Inicio;
        public bool Horizontal => Orientacao == Orientacao.Horizontal;

        public (double X, double Y) Ponto(double t) => Horizontal ? (t, Coord) : (Coord, t);

        /// <summary>Coordenada ao longo do eixo de um ponto qualquer.</summary>
        public double AoLongo(double x, double y) => Horizontal ? x : y;

        /// <summary>Coordenada transversal (perpendicular ao eixo) de um ponto qualquer.</summary>
        public double Transversal(double x, double y) => Horizontal ? y : x;

        /// <summary>Vãos que interrompem a alvenaria na 1ª/2ª fiada (portas e passagens; janelas não).</summary>
        public IEnumerable<Vao> VaosQueCortam() =>
            Vaos.Where(v => v.Tipo != TipoVao.Janela).OrderBy(v => v.Inicio);

        public override string ToString() =>
            $"{Nome ?? "#" + Indice} {(Horizontal ? "H" : "V")} c={Coord:0.#} [{Inicio:0.#}; {Fim:0.#}] e={Espessura:0.#}";
    }

    public enum TipoEncontro
    {
        L,
        T,
        X,
        /// <summary>Paredes colineares que se tocam pela ponta, ou caso não reconhecido.</summary>
        Outro
    }

    /// <summary>Encontro entre paredes, no cruzamento dos eixos.</summary>
    public sealed class Encontro
    {
        public double X { get; set; }
        public double Y { get; set; }

        /// <summary>Paredes que terminam neste encontro.</summary>
        public List<Parede> Terminais { get; } = new List<Parede>();

        /// <summary>Paredes que continuam através do encontro.</summary>
        public List<Parede> Passantes { get; } = new List<Parede>();

        public TipoEncontro Tipo { get; set; }

        public IEnumerable<Parede> Paredes => Passantes.Concat(Terminais);

        /// <summary>
        /// Parede que passa direto na fiada (amarração direta): na fiada ímpar a principal
        /// (passante; senão a maior — questionário, item 17), na fiada par a seguinte, alternando.
        /// </summary>
        public Parede Dominante(int fiada)
        {
            var ordem = Passantes.OrderByDescending(p => p.Comprimento)
                .Concat(Terminais.OrderByDescending(p => p.Comprimento))
                .ToList();
            if (ordem.Count == 0) return null;
            if (fiada % 2 == 1 || ordem.Count == 1) return ordem[0];
            // Na fiada par passa uma parede perpendicular à dominante da ímpar.
            var primeira = ordem[0];
            return ordem.Skip(1).FirstOrDefault(p => p.Orientacao != primeira.Orientacao) ?? ordem[1];
        }

        public override string ToString() => $"{Tipo} ({X:0.#}, {Y:0.#})";
    }

    public sealed class PlantaDetectada
    {
        public List<Parede> Paredes { get; } = new List<Parede>();
        public List<Encontro> Encontros { get; } = new List<Encontro>();
        public List<string> Avisos { get; } = new List<string>();

        public IEnumerable<Encontro> EncontrosDe(Parede p) => Encontros.Where(e => e.Paredes.Contains(p));
    }

    public sealed class OpcoesDeteccao
    {
        public HashSet<string> LayersParede { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> LayersPorta { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> LayersJanela { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Faixa de espessura (cm) entre duas faces paralelas para serem consideradas parede.</summary>
        public double EspessuraMinima { get; set; } = 5;
        public double EspessuraMaxima { get; set; } = 35;

        /// <summary>Maior vão de janela aceito (cm); acima disso o buraco separa paredes.</summary>
        public double JanelaMaxima { get; set; } = 600;

        /// <summary>Distância máxima (cm) de um rótulo P*/J* ao centro do vão para decidir o tipo dele.</summary>
        public double DistanciaRotulo { get; set; } = 120;

        /// <summary>Paredes mais curtas que isso são ignoradas (cm).</summary>
        public double ComprimentoMinimo { get; set; } = 5;

        /// <summary>Maior vão sem porta/janela que ainda é tratado como passagem na mesma parede (cm).</summary>
        public double PassagemMaxima { get; set; } = 120;

        /// <summary>Tolerância geral de desenho (cm).</summary>
        public double Tolerancia { get; set; } = 1;
    }
}
