using System.Collections.Generic;
using System.Linq;

namespace AlvMod.Core.Catalogo
{
    public enum Material
    {
        Concreto,
        Ceramico
    }

    public sealed class Familia
    {
        public string Nome { get; set; }
        public Material Material { get; set; }

        /// <summary>Espessura da junta vertical, em cm.</summary>
        public int Junta { get; set; }

        /// <summary>Módulo nominal (peça principal + junta), em cm.</summary>
        public int Modulo { get; set; }

        public List<Peca> Pecas { get; set; } = new List<Peca>();

        /// <summary>Peças que o motor pode usar numa fiada comum (habilitadas, sem canaletas).</summary>
        public IEnumerable<Peca> PecasDeFiada() =>
            Pecas.Where(p => p.Habilitada && p.Funcao != FuncaoPeca.Canaleta);

        public Peca Buscar(string codigo) => Pecas.FirstOrDefault(p => p.Codigo == codigo);
    }
}
