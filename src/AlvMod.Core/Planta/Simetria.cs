using System;
using System.Collections.Generic;
using System.Linq;

namespace AlvMod.Core.Planta
{
    /// <summary>
    /// Espelhamentos do pavimento (prédio com apartamentos espelhados). O engenheiro modula
    /// "tipos": apartamentos espelhados recebem a modulação espelhada. Aqui são achados os eixos de simetria
    /// (vertical e/ou horizontal) que levam cada parede, porta e encontro em outro igual; o planejador decide
    /// cada grupo de encontros/portas espelhados de uma vez, e o resultado sai simétrico.
    /// </summary>
    public sealed class Simetria
    {
        /// <summary>Grupos de encontros que são o mesmo encontro espelhado (o primeiro representa o grupo).</summary>
        public List<List<Encontro>> Encontros { get; } = new List<List<Encontro>>();

        /// <summary>
        /// Grupos de portas espelhadas. O deslocamento de cada uma é sinal × (deslocamento da primeira) + delta:
        /// o espelho pode inverter o sentido da parede, e a arquitetura nunca é espelhada ao milímetro.
        /// </summary>
        public List<List<(Parede parede, Vao vao, int sinal, double delta)>> Portas { get; } =
            new List<List<(Parede, Vao, int, double)>>();

        /// <summary>Eixos de simetria encontrados, para o relatório (ex.: "x = 982,5").</summary>
        public List<string> Eixos { get; } = new List<string>();

        private sealed class Espelho
        {
            public bool EmX;        // espelha x (eixo vertical de simetria)
            public double Centro;
            public (double x, double y) Ponto(double x, double y) => EmX ? (2 * Centro - x, y) : (x, 2 * Centro - y);
            public double AoLongo(Parede w, double t) => Inverte(w) ? 2 * Centro - t : t;
            // A coordenada ao longo da parede inverte quando a parede é perpendicular ao eixo de simetria.
            public bool Inverte(Parede w) => EmX ? w.Horizontal : !w.Horizontal;
        }

        public static Simetria Detectar(PlantaDetectada planta, double tolerancia = 2)
        {
            var s = new Simetria();
            var espelhos = new List<Espelho>();
            var verticais = planta.Paredes.Where(p => !p.Horizontal).Select(p => p.Coord).ToList();
            var horizontais = planta.Paredes.Where(p => p.Horizontal).Select(p => p.Coord).ToList();
            if (verticais.Count > 0)
            {
                var e = new Espelho { EmX = true, Centro = (verticais.Min() + verticais.Max()) / 2 };
                if (Valido(planta, e, tolerancia)) { espelhos.Add(e); s.Eixos.Add($"x = {e.Centro:0.#}"); }
            }
            if (horizontais.Count > 0)
            {
                var e = new Espelho { EmX = false, Centro = (horizontais.Min() + horizontais.Max()) / 2 };
                if (Valido(planta, e, tolerancia)) { espelhos.Add(e); s.Eixos.Add($"y = {e.Centro:0.#}"); }
            }

            // Encontros: grupos fechados pelos espelhos.
            var visto = new HashSet<Encontro>();
            foreach (var e in planta.Encontros)
            {
                if (visto.Contains(e)) continue;
                var grupo = new List<Encontro> { e };
                visto.Add(e);
                for (int i = 0; i < grupo.Count; i++)
                    foreach (var m in espelhos)
                    {
                        var img = Imagem(planta, grupo[i], m, tolerancia);
                        if (img != null && visto.Add(img)) grupo.Add(img);
                    }
                s.Encontros.Add(grupo);
            }

            // Portas (e passagens): o deslocamento troca de sinal quando o espelho inverte a parede.
            var portas = planta.Paredes.SelectMany(w => w.VaosQueCortam().Select(v => (w, v))).ToList();
            var vistaPorta = new HashSet<Vao>();
            foreach (var (w, v) in portas)
            {
                if (vistaPorta.Contains(v)) continue;
                var grupo = new List<(Parede, Vao, int, double)> { (w, v, 1, 0.0) };
                vistaPorta.Add(v);
                for (int i = 0; i < grupo.Count; i++)
                    foreach (var m in espelhos)
                    {
                        var (gw, gv, sinal, delta) = grupo[i];
                        var img = Imagem(planta, gw, gv, m, tolerancia);
                        if (img == null || !vistaPorta.Add(img.Value.vao)) continue;
                        // Centro deslocado da imagem = espelho do centro deslocado desta porta:
                        // c1 + d1 = espelho(c0 + d0), com d0 = sinal·s + delta.
                        int k = m.Inverte(gw) ? -1 : 1;
                        double d1Base = m.AoLongo(gw, gv.Centro + delta) - img.Value.vao.Centro; // parte que não depende de s
                        grupo.Add((img.Value.parede, img.Value.vao, k * sinal, d1Base));
                    }
                s.Portas.Add(grupo);
            }
            return s;
        }

        private static bool Valido(PlantaDetectada planta, Espelho m, double tol)
        {
            foreach (var w in planta.Paredes)
            {
                var img = ImagemParede(planta, w, m, tol);
                if (img == null) return false;
                foreach (var v in w.VaosQueCortam())
                    if (Imagem(planta, w, v, m, tol) == null) return false;
            }
            return planta.Encontros.All(e => Imagem(planta, e, m, tol) != null);
        }

        private static Parede ImagemParede(PlantaDetectada planta, Parede w, Espelho m, double tol)
        {
            var (x1, y1) = m.Ponto(w.Ponto(w.Inicio).X, w.Ponto(w.Inicio).Y);
            var (x2, y2) = m.Ponto(w.Ponto(w.Fim).X, w.Ponto(w.Fim).Y);
            double coord = w.Horizontal ? y1 : x1;
            double a = w.Horizontal ? Math.Min(x1, x2) : Math.Min(y1, y2);
            double b = w.Horizontal ? Math.Max(x1, x2) : Math.Max(y1, y2);
            return planta.Paredes.FirstOrDefault(p => p.Orientacao == w.Orientacao && Math.Abs(p.Coord - coord) <= tol &&
                                                      Math.Abs(p.Inicio - a) <= tol && Math.Abs(p.Fim - b) <= tol);
        }

        private static (Parede parede, Vao vao)? Imagem(PlantaDetectada planta, Parede w, Vao v, Espelho m, double tol)
        {
            var iw = ImagemParede(planta, w, m, tol);
            if (iw == null) return null;
            var (px, py) = w.Ponto(v.Centro);
            var (qx, qy) = m.Ponto(px, py);
            double c = iw.AoLongo(qx, qy);
            var iv = iw.VaosQueCortam().FirstOrDefault(x => x.Tipo == v.Tipo && Math.Abs(x.Centro - c) <= tol + 1 &&
                                                            Math.Abs(x.Largura - v.Largura) <= 2);
            return iv == null ? ((Parede, Vao)?)null : (iw, iv);
        }

        private static Encontro Imagem(PlantaDetectada planta, Encontro e, Espelho m, double tol)
        {
            var (x, y) = m.Ponto(e.X, e.Y);
            return planta.Encontros.FirstOrDefault(o => o.Tipo == e.Tipo && Math.Abs(o.X - x) <= tol && Math.Abs(o.Y - y) <= tol);
        }
    }
}
