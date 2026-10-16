using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace AlvMod.Plugin.Desenho
{
    /// <summary>Trecho reto do eixo de uma parede, em unidades do desenho.</summary>
    internal sealed class Trecho
    {
        public Point3d Inicio { get; }
        public Point3d Fim { get; }
        public Vector3d Direcao { get; }
        public Vector3d Perpendicular { get; }
        public double Comprimento { get; }

        public Trecho(Point3d inicio, Point3d fim)
        {
            Inicio = inicio;
            Fim = fim;
            Comprimento = inicio.DistanceTo(fim);
            Direcao = (fim - inicio).GetNormal();
            Perpendicular = new Vector3d(-Direcao.Y, Direcao.X, 0);
        }

        /// <summary>
        /// Mesmo trecho com início na ponta esquerda (ou inferior, se vertical), seguindo a origem
        /// "canto inferior esquerdo" do questionário (item 23).
        /// </summary>
        public Trecho Normalizado()
        {
            const double eps = 1e-9;
            bool inverter = Direcao.X < -eps || (Math.Abs(Direcao.X) <= eps && Direcao.Y < 0);
            return inverter ? new Trecho(Fim, Inicio) : this;
        }

        /// <summary>Posição ao longo do eixo (t) e afastamento lateral (d) de um ponto.</summary>
        public void Projetar(Point3d p, out double t, out double d)
        {
            var v = p - Inicio;
            t = v.DotProduct(Direcao);
            d = v.DotProduct(Perpendicular);
        }

        public Point3d PontoEm(double distancia) => Inicio + Direcao * distancia;

        /// <summary>Trecho encolhido nas pontas (cm), para abrir espaço à folga de marco das aberturas.</summary>
        public Trecho Encolher(double inicioCm, double fimCm, double fatorCm) =>
            inicioCm <= 0 && fimCm <= 0
                ? this
                : new Trecho(PontoEm(inicioCm / fatorCm), PontoEm(Comprimento - fimCm / fatorCm));

        public string Descrever() => string.Format(CultureInfo.InvariantCulture,
            "de ({0:0.##}, {1:0.##}) a ({2:0.##}, {3:0.##})", Inicio.X, Inicio.Y, Fim.X, Fim.Y);
    }

    internal static class Geometria
    {
        /// <summary>
        /// Pede a parede. Padrão (arquitetura real: paredes em linha dupla, sem eixo, dentro de bloco):
        /// dois pontos numa face (início e fim, com OSNAP) e um ponto na face oposta; o eixo é a linha
        /// média. Opções: Eixo (clicar uma linha de eixo) ou Faces (clicar as duas linhas de face).
        /// Linhas dentro de blocos (ex.: bloco ARQUITETURA) podem ser clicadas diretamente.
        /// </summary>
        /// <param name="espessura">
        /// Vetor da face clicada até o eixo, lembrado entre segmentos da mesma parede (Enter reaproveita).
        /// </param>
        public static Trecho SelecionarTrecho(Editor ed, Transaction tr, string mensagem, ref Vector3d? espessura)
        {
            var o = new PromptPointOptions(mensagem + ": início da parede, numa face [Eixo/Faces]: ");
            o.Keywords.Add("Eixo");
            o.Keywords.Add("Faces");
            o.AppendKeywordsToMessage = false;
            o.AllowNone = true;

            var r = ed.GetPoint(o);
            if (r.Status == PromptStatus.Keyword)
                return r.StringResult == "Eixo"
                    ? SelecionarSegmento(ed, tr, "\nSelecione a linha do eixo da parede: ")
                    : SelecionarPorFaces(ed, tr);
            if (r.Status != PromptStatus.OK) return null;

            var ucs = ed.CurrentUserCoordinateSystem;
            var a = r.Value.TransformBy(ucs);
            var rb = ed.GetPoint(new PromptPointOptions("\nFim da parede, na mesma face: ") { BasePoint = r.Value, UseBasePoint = true });
            if (rb.Status != PromptStatus.OK) return null;
            var b = rb.Value.TransformBy(ucs);
            if (a.DistanceTo(b) < 1e-6) return null;

            var face = new Trecho(a, b);
            var oc = new PromptPointOptions(espessura.HasValue
                ? "\nUm ponto na face oposta <mesma espessura do segmento anterior>: "
                : "\nUm ponto na face oposta: ")
            {
                BasePoint = rb.Value,
                UseBasePoint = true,
                AllowNone = espessura.HasValue
            };
            var rc = ed.GetPoint(oc);
            if (rc.Status == PromptStatus.OK)
            {
                face.Projetar(rc.Value.TransformBy(ucs), out _, out double d);
                espessura = face.Perpendicular * (d / 2);
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\nEspessura da parede no desenho: {0:0.###} unidades.", Math.Abs(d)));
            }
            else if (rc.Status != PromptStatus.None || !espessura.HasValue)
            {
                return null;
            }

            return new Trecho(a + espessura.Value, b + espessura.Value);
        }

        private static Trecho SelecionarPorFaces(Editor ed, Transaction tr)
        {
            var face1 = SelecionarSegmento(ed, tr, "\nSelecione a 1ª face da parede: ");
            if (face1 == null) return null;
            var face2 = SelecionarSegmento(ed, tr, "\nSelecione a 2ª face (paralela): ");
            if (face2 == null) return null;

            if (Math.Abs(face1.Direcao.DotProduct(face2.Direcao)) < Math.Cos(2 * Math.PI / 180))
            {
                ed.WriteMessage("\nAs faces não são paralelas.");
                return null;
            }

            // Eixo = média entre cada ponta da face 1 e a projeção dela sobre a reta da face 2.
            var reta2 = new Line3d(face2.Inicio, face2.Fim);
            var a = face1.Inicio;
            var b = face1.Fim;
            var meioA = a + (reta2.GetClosestPointTo(a).Point - a) / 2;
            var meioB = b + (reta2.GetClosestPointTo(b).Point - b) / 2;
            double espessura = a.DistanceTo(reta2.GetClosestPointTo(a).Point);
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\nEixo calculado entre as faces (espessura {0:0.###} unidades).", espessura));
            return new Trecho(meioA, meioB);
        }

        /// <summary>LINE ou segmento reto de POLYLINE, inclusive dentro de blocos (seleção aninhada).</summary>
        private static Trecho SelecionarSegmento(Editor ed, Transaction tr, string mensagem)
        {
            var res = ed.GetNestedEntity(new PromptNestedEntityOptions(mensagem));
            if (res.Status != PromptStatus.OK) return null;

            var ent = (Entity)tr.GetObject(res.ObjectId, OpenMode.ForRead);
            var paraDesenho = res.Transform; // coordenadas do bloco → coordenadas do desenho
            if (ent is Line linha)
                return new Trecho(linha.StartPoint.TransformBy(paraDesenho), linha.EndPoint.TransformBy(paraDesenho));

            if (!(ent is Polyline pl))
            {
                ed.WriteMessage("\nSelecione uma LINE ou POLYLINE.");
                return null;
            }

            var clique = res.PickedPoint.TransformBy(ed.CurrentUserCoordinateSystem).TransformBy(paraDesenho.Inverse());
            var maisProximo = pl.GetClosestPointTo(clique, Vector3d.ZAxis, false);
            int segmentos = pl.Closed ? pl.NumberOfVertices : pl.NumberOfVertices - 1;
            int i = Math.Min(Math.Max((int)Math.Floor(pl.GetParameterAtPoint(maisProximo)), 0), segmentos - 1);

            if (pl.GetSegmentType(i) != SegmentType.Line)
            {
                ed.WriteMessage("\nO segmento selecionado é um arco. Só paredes retas são suportadas por enquanto.");
                return null;
            }
            var seg = pl.GetLineSegmentAt(i);
            if (segmentos > 1)
                ed.WriteMessage($"\nPolilinha com {segmentos} segmentos: usando o segmento {i + 1}, o mais próximo do clique.");
            return new Trecho(seg.StartPoint.TransformBy(paraDesenho), seg.EndPoint.TransformBy(paraDesenho));
        }

        /// <summary>Cantos de uma peça desenhada (bloco DWG ou retângulo POLYLINE), em coordenadas do desenho.</summary>
        public static List<Point3d> Cantos(Entity ent, Transaction tr)
        {
            var pontos = new List<Point3d>();
            if (ent is Polyline pl)
            {
                // Só retângulos fechados: ignora o próprio eixo e outras polilinhas que vierem na seleção.
                if (!pl.Closed || pl.NumberOfVertices != 4) return pontos;
                for (int i = 0; i < pl.NumberOfVertices; i++) pontos.Add(pl.GetPoint3dAt(i));
            }
            else if (ent is BlockReference br)
            {
                // Extensão da definição do bloco, transformada pela inserção (funciona em parede inclinada).
                var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                var ext = new Extents3d();
                bool vazio = true;
                foreach (ObjectId id in btr)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity filho) || filho is AttributeDefinition) continue;
                    try
                    {
                        if (vazio) { ext = filho.GeometricExtents; vazio = false; }
                        else ext.AddExtents(filho.GeometricExtents);
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { /* entidade sem extensão */ }
                }
                if (vazio) return pontos;

                var min = ext.MinPoint;
                var max = ext.MaxPoint;
                foreach (var p in new[]
                         {
                             new Point3d(min.X, min.Y, 0), new Point3d(max.X, min.Y, 0),
                             new Point3d(max.X, max.Y, 0), new Point3d(min.X, max.Y, 0)
                         })
                    pontos.Add(p.TransformBy(br.BlockTransform));
            }
            return pontos;
        }
    }
}
