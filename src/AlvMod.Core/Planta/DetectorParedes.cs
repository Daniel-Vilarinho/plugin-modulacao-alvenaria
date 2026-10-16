using System;
using System.Collections.Generic;
using System.Linq;

namespace AlvMod.Core.Planta
{
    /// <summary>
    /// Lê a planta de arquitetura (paredes desenhadas pelas duas faces, ortogonais) e devolve as paredes
    /// pelo eixo, com vãos (portas, janelas, passagens) e encontros (L, T, X).
    ///
    /// Passos:
    ///  1. Linhas do layer de parede → horizontais e verticais, colineares unidas.
    ///  2. Faces paralelas a uma distância de parede (5–40 cm) → faixas de parede (eixo no meio).
    ///  3. Faixas no mesmo eixo unidas: o buraco entre elas é encontro (parede perpendicular passando),
    ///     janela (linhas do layer de janela), porta (linhas do layer de porta), passagem ou fim de parede.
    ///  4. Pontas de parede encostadas numa parede perpendicular são estendidas até o eixo dela → encontros.
    /// </summary>
    public static class DetectorParedes
    {
        private sealed class Linha
        {
            public double C, A, B; // C = coordenada fixa; [A, B] = intervalo ao longo
        }

        private sealed class Faixa
        {
            public double C, A, B, Esp;
        }

        public static PlantaDetectada Detectar(IEnumerable<Segmento> segmentos, OpcoesDeteccao op,
            IEnumerable<Rotulo> rotulos = null)
        {
            var planta = new PlantaDetectada();
            var lista = segmentos.ToList();

            var h = new List<Linha>();
            var v = new List<Linha>();
            int inclinadas = 0;
            foreach (var s in lista.Where(s => op.LayersParede.Contains(s.Layer ?? "")))
            {
                double dx = s.X2 - s.X1, dy = s.Y2 - s.Y1, len = s.Comprimento;
                if (len < 0.5) continue;
                if (Math.Abs(dy) <= len * 0.005)
                    h.Add(new Linha { C = (s.Y1 + s.Y2) / 2, A = Math.Min(s.X1, s.X2), B = Math.Max(s.X1, s.X2) });
                else if (Math.Abs(dx) <= len * 0.005)
                    v.Add(new Linha { C = (s.X1 + s.X2) / 2, A = Math.Min(s.Y1, s.Y2), B = Math.Max(s.Y1, s.Y2) });
                else
                    inclinadas++;
            }
            if (inclinadas > 0)
                planta.Avisos.Add($"{inclinadas} linha(s) de parede inclinada(s) ignorada(s): só paredes ortogonais são lidas automaticamente.");

            h = UnirColineares(h, op.Tolerancia);
            v = UnirColineares(v, op.Tolerancia);
            var faixasH = Parear(h, op);
            var faixasV = Parear(v, op);

            var ctx = new ContextoVaos
            {
                Portas = Pontos(lista, op.LayersPorta),
                Janelas = lista.Where(s => op.LayersJanela.Contains(s.Layer ?? "")).ToList(),
                Rotulos = (rotulos ?? Enumerable.Empty<Rotulo>())
                    .Where(r => TipoPorRotulo(r.Texto).HasValue).ToList()
            };

            var paredes = new List<Parede>();
            paredes.AddRange(Unir(faixasH, faixasV, Orientacao.Horizontal, ctx, op));
            paredes.AddRange(Unir(faixasV, faixasH, Orientacao.Vertical, ctx, op));
            paredes = paredes.Where(p => p.Comprimento >= op.ComprimentoMinimo).ToList();
            for (int i = 0; i < paredes.Count; i++) paredes[i].Indice = i;
            planta.Paredes.AddRange(paredes);

            DetectarEncontros(planta, op);
            return planta;
        }

        // ------------------------------------------------------------------ passo 1

        private static List<Linha> UnirColineares(List<Linha> linhas, double tol)
        {
            var resultado = new List<Linha>();
            foreach (var grupo in Agrupar(linhas.OrderBy(l => l.C).ToList(), l => l.C, tol / 2))
            {
                double c = grupo.Average(l => l.C);
                Linha atual = null;
                foreach (var l in grupo.OrderBy(l => l.A))
                {
                    if (atual != null && l.A <= atual.B + tol / 2)
                    {
                        atual.B = Math.Max(atual.B, l.B);
                        continue;
                    }
                    atual = new Linha { C = c, A = l.A, B = l.B };
                    resultado.Add(atual);
                }
            }
            return resultado;
        }

        // ------------------------------------------------------------------ passo 2

        /// <summary>
        /// Para cada linha, a face parceira é a linha paralela mais próxima acima (na faixa de espessura de
        /// parede); cada trecho da linha só é usado uma vez, com o parceiro mais próximo.
        /// </summary>
        private static List<Faixa> Parear(List<Linha> linhas, OpcoesDeteccao op)
        {
            var faixas = new List<Faixa>();
            var ordenadas = linhas.OrderBy(l => l.C).ToList();
            for (int i = 0; i < ordenadas.Count; i++)
            {
                var baixo = ordenadas[i];
                var livres = new List<(double a, double b)> { (baixo.A, baixo.B) };
                var candidatas = ordenadas.Skip(i + 1)
                    .Where(c => c.C - baixo.C >= op.EspessuraMinima && c.C - baixo.C <= op.EspessuraMaxima)
                    .OrderBy(c => c.C - baixo.C);
                foreach (var cima in candidatas)
                {
                    var novos = new List<(double a, double b)>();
                    foreach (var (a, b) in livres)
                    {
                        double ia = Math.Max(a, cima.A), ib = Math.Min(b, cima.B);
                        if (ib - ia >= op.Tolerancia)
                        {
                            faixas.Add(new Faixa { C = (baixo.C + cima.C) / 2, A = ia, B = ib, Esp = cima.C - baixo.C });
                            if (ia - a > op.Tolerancia) novos.Add((a, ia));
                            if (b - ib > op.Tolerancia) novos.Add((ib, b));
                        }
                        else
                        {
                            novos.Add((a, b));
                        }
                    }
                    livres = novos;
                    if (livres.Count == 0) break;
                }
            }
            return faixas;
        }

        // ------------------------------------------------------------------ passo 3

        private sealed class ContextoVaos
        {
            public List<(double x, double y)> Portas;
            public List<Segmento> Janelas;
            public List<Rotulo> Rotulos;
        }

        private static IEnumerable<Parede> Unir(List<Faixa> faixas, List<Faixa> perpendiculares, Orientacao orientacao,
            ContextoVaos ctx, OpcoesDeteccao op)
        {
            bool hor = orientacao == Orientacao.Horizontal;
            // Mesmo eixo (±tolerância) e espessura parecida (±2 cm) = mesma linha de parede.
            foreach (var mesmoEixo in Agrupar(faixas.OrderBy(f => f.C).ToList(), f => f.C, op.Tolerancia))
            {
                foreach (var grupo in Agrupar(mesmoEixo.OrderBy(f => f.Esp).ToList(), f => f.Esp, 2))
                {
                    double c = grupo.Average(f => f.C);
                    double esp = grupo.Average(f => f.Esp);
                    Parede atual = null;
                    foreach (var f in grupo.OrderBy(f => f.A))
                    {
                        if (atual == null)
                        {
                            atual = Nova(orientacao, c, f.A, f.B, esp);
                            continue;
                        }

                        double ga = atual.Fim, gb = f.A, gap = gb - ga;
                        if (gap <= op.Tolerancia)
                        {
                            atual.Fim = Math.Max(atual.Fim, f.B);
                            continue;
                        }

                        var tipo = ClassificarBuraco(hor, c, esp, ga, gb, perpendiculares, ctx, op);
                        if (tipo == Buraco.Encontro)
                        {
                            atual.Fim = Math.Max(atual.Fim, f.B);
                        }
                        else if (tipo == Buraco.Separa)
                        {
                            yield return atual;
                            atual = Nova(orientacao, c, f.A, f.B, esp);
                        }
                        else
                        {
                            var tv = tipo == Buraco.Porta ? TipoVao.Porta : tipo == Buraco.Janela ? TipoVao.Janela : TipoVao.Passagem;
                            atual.Vaos.Add(new Vao { Tipo = tv, Inicio = ga, Fim = gb });
                            atual.Fim = Math.Max(atual.Fim, f.B);
                        }
                    }
                    if (atual != null) yield return atual;
                }
            }
        }

        private enum Buraco { Encontro, Porta, Janela, Passagem, Separa }

        /// <summary>
        /// O que é o buraco [ga, gb] entre duas faixas do mesmo eixo:
        ///  - encontro: uma parede perpendicular, da mesma largura do buraco, chega/passa ali;
        ///  - porta/janela pelo rótulo mais próximo (P1, J2…), quando há linhas de porta/janela no vão;
        ///  - janela: linhas do layer de janela PARALELAS à parede cobrindo ao menos metade do vão;
        ///  - porta: qualquer linha do layer de porta dentro do vão;
        ///  - passagem: buraco pequeno sem nada; senão, são paredes separadas.
        /// </summary>
        private static Buraco ClassificarBuraco(bool hor, double c, double esp, double ga, double gb,
            List<Faixa> perpendiculares, ContextoVaos ctx, OpcoesDeteccao op)
        {
            double gap = gb - ga;
            bool encontro = perpendiculares.Any(p =>
                p.C >= ga - op.Tolerancia && p.C <= gb + op.Tolerancia &&
                Math.Abs(gap - p.Esp) <= 3 &&
                p.A - esp / 2 - 3 <= c && c <= p.B + esp / 2 + 3);
            if (encontro) return Buraco.Encontro;

            double t0 = c - esp / 2 - 2, t1 = c + esp / 2 + 2;
            double Ao(double x, double y) => hor ? x : y;
            double Tr(double x, double y) => hor ? y : x;

            bool temPorta = ctx.Portas.Any(p =>
                Ao(p.x, p.y) >= ga - 2 && Ao(p.x, p.y) <= gb + 2 && Tr(p.x, p.y) >= t0 && Tr(p.x, p.y) <= t1);

            double cobertura = 0;
            foreach (var s in ctx.Janelas)
            {
                bool paralela = hor ? Math.Abs(s.Y2 - s.Y1) <= 0.5 : Math.Abs(s.X2 - s.X1) <= 0.5;
                if (!paralela || Tr(s.X1, s.Y1) < t0 || Tr(s.X1, s.Y1) > t1) continue;
                double a = Math.Max(ga, Math.Min(Ao(s.X1, s.Y1), Ao(s.X2, s.Y2)));
                double b = Math.Min(gb, Math.Max(Ao(s.X1, s.Y1), Ao(s.X2, s.Y2)));
                cobertura = Math.Max(cobertura, b - a);
            }
            bool temJanela = gap <= op.JanelaMaxima && cobertura >= gap / 2;

            if (temPorta || temJanela)
            {
                // O rótulo do projeto decide na dúvida (ex.: porta de vidro desenhada como esquadria).
                double mx = hor ? (ga + gb) / 2 : c, my = hor ? c : (ga + gb) / 2;
                var rotulo = ctx.Rotulos
                    .Select(r => (r, d: Math.Sqrt((r.X - mx) * (r.X - mx) + (r.Y - my) * (r.Y - my))))
                    .Where(x => x.d <= op.DistanciaRotulo)
                    .OrderBy(x => x.d)
                    .Select(x => TipoPorRotulo(x.r.Texto))
                    .FirstOrDefault();
                if (rotulo == TipoVao.Porta) return Buraco.Porta;
                if (rotulo == TipoVao.Janela) return Buraco.Janela;
                return temPorta ? Buraco.Porta : Buraco.Janela;
            }

            bool cruzaParede = perpendiculares.Any(p => p.C > ga && p.C < gb && p.A - esp <= c && c <= p.B + esp);
            if (gap <= op.PassagemMaxima && !cruzaParede) return Buraco.Passagem;
            return Buraco.Separa;
        }

        /// <summary>Convenção dos projetos: P1, P2… = porta; J1, J2… = janela.</summary>
        private static TipoVao? TipoPorRotulo(string texto)
        {
            var t = (texto ?? "").Trim().ToUpperInvariant();
            if (t.Length < 2 || !char.IsDigit(t[1])) return null;
            if (t[0] == 'P') return TipoVao.Porta;
            if (t[0] == 'J') return TipoVao.Janela;
            return null;
        }

        private static Parede Nova(Orientacao o, double c, double a, double b, double esp) =>
            new Parede { Orientacao = o, Coord = c, Inicio = a, Fim = b, Espessura = esp };

        /// <summary>Pontas e meio de cada segmento dos layers indicados (para achar portas e janelas nos vãos).</summary>
        private static List<(double x, double y)> Pontos(List<Segmento> segmentos, HashSet<string> layers)
        {
            var pts = new List<(double, double)>();
            foreach (var s in segmentos.Where(s => layers.Contains(s.Layer ?? "")))
            {
                pts.Add((s.X1, s.Y1));
                pts.Add((s.X2, s.Y2));
                pts.Add(((s.X1 + s.X2) / 2, (s.Y1 + s.Y2) / 2));
            }
            return pts;
        }

        // ------------------------------------------------------------------ passo 4

        private static void DetectarEncontros(PlantaDetectada planta, OpcoesDeteccao op)
        {
            const double tolEncontro = 3;
            var paredes = planta.Paredes;
            var pontos = new List<(double x, double y)>();

            // Pontas encostadas numa parede perpendicular: estende até o eixo dela.
            var originais = paredes.ToDictionary(p => p, p => (p.Inicio, p.Fim));
            foreach (var w in paredes)
            {
                foreach (bool noInicio in new[] { true, false })
                {
                    double t = noInicio ? originais[w].Inicio : originais[w].Fim;
                    Parede alvo = null;
                    double melhor = double.MaxValue;
                    foreach (var p in paredes.Where(p => p.Orientacao != w.Orientacao))
                    {
                        var (pi, pf) = originais[p];
                        double folgaAoLongo = w.Espessura / 2 + tolEncontro;
                        if (w.Coord < pi - folgaAoLongo || w.Coord > pf + folgaAoLongo) continue;
                        double d = Math.Abs(t - p.Coord);
                        if (d <= p.Espessura / 2 + tolEncontro && d < melhor)
                        {
                            melhor = d;
                            alvo = p;
                        }
                    }
                    if (alvo == null) continue;

                    if (noInicio) w.Inicio = Math.Min(w.Inicio, alvo.Coord);
                    else w.Fim = Math.Max(w.Fim, alvo.Coord);
                    pontos.Add(w.Ponto(alvo.Coord));
                }
            }

            // Cruzamentos em que as duas paredes continuam (X) ou pontas já sobre o eixo de outra.
            foreach (var a in paredes.Where(p => p.Horizontal))
                foreach (var b in paredes.Where(p => !p.Horizontal))
                    if (b.Coord >= a.Inicio - op.Tolerancia && b.Coord <= a.Fim + op.Tolerancia &&
                        a.Coord >= b.Inicio - op.Tolerancia && a.Coord <= b.Fim + op.Tolerancia)
                        pontos.Add((b.Coord, a.Coord));

            foreach (var (x, y) in Unicos(pontos, 2))
            {
                var e = new Encontro { X = x, Y = y };
                foreach (var p in paredes)
                {
                    double ao = p.AoLongo(x, y), tr = p.Transversal(x, y);
                    if (Math.Abs(tr - p.Coord) > op.Tolerancia + 1) continue;
                    if (ao < p.Inicio - 2 || ao > p.Fim + 2) continue;
                    bool ponta = Math.Abs(ao - p.Inicio) <= 2 || Math.Abs(ao - p.Fim) <= 2;
                    (ponta ? e.Terminais : e.Passantes).Add(p);
                }
                if (e.Terminais.Count + e.Passantes.Count < 2) continue;
                e.Tipo = Classificar(e);
                planta.Encontros.Add(e);
            }
        }

        private static TipoEncontro Classificar(Encontro e)
        {
            bool perpendiculares = e.Paredes.Select(p => p.Orientacao).Distinct().Count() == 2;
            if (!perpendiculares) return TipoEncontro.Outro;
            int p = e.Passantes.Count, t = e.Terminais.Count;
            if (p == 0 && t == 2) return TipoEncontro.L;
            if (p == 0 && t == 3) return TipoEncontro.T; // duas colineares terminando + uma perpendicular
            if (p == 1 && t == 1) return TipoEncontro.T;
            if (p >= 2 || (p == 1 && t >= 2) || t >= 4) return TipoEncontro.X;
            return TipoEncontro.Outro;
        }

        // ------------------------------------------------------------------ utilidades

        private static IEnumerable<List<T>> Agrupar<T>(List<T> ordenados, Func<T, double> chave, double tol)
        {
            var grupo = new List<T>();
            double? ultimo = null;
            foreach (var item in ordenados)
            {
                double k = chave(item);
                if (ultimo.HasValue && k - ultimo.Value > tol)
                {
                    yield return grupo;
                    grupo = new List<T>();
                }
                grupo.Add(item);
                ultimo = k;
            }
            if (grupo.Count > 0) yield return grupo;
        }

        private static IEnumerable<(double x, double y)> Unicos(List<(double x, double y)> pontos, double tol)
        {
            var unicos = new List<(double x, double y)>();
            foreach (var p in pontos)
                if (!unicos.Any(u => Math.Abs(u.x - p.x) <= tol && Math.Abs(u.y - p.y) <= tol))
                    unicos.Add(p);
            return unicos;
        }
    }
}
