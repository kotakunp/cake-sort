import SwiftUI

extension Color {
    init(hex: UInt32) {
        self.init(
            .sRGB,
            red: Double((hex >> 16) & 0xFF) / 255.0,
            green: Double((hex >> 8) & 0xFF) / 255.0,
            blue: Double(hex & 0xFF) / 255.0,
            opacity: 1
        )
    }
}

enum FlavorPalette {
    static let backgroundTop = Color(hex: 0xFFF7EA)
    static let backgroundBottom = Color(hex: 0xFFDFB8)
    static let ink = Color(hex: 0x54342A)
    static let accent = Color(hex: 0xF57C00)
    static let accentLight = Color(hex: 0xFFB74D)
    static let plateLight = Color(hex: 0xFFFFFF)
    static let plateDark = Color(hex: 0xEDDCBF)
    static let trayTop = Color(hex: 0xE9B981)
    static let trayBottom = Color(hex: 0xC98D4E)
    static let trayEdge = Color(hex: 0xA9713A)

    /// Light / base / dark stops per flavor so wedges can be shaded like frosting.
    struct Shade {
        let light: Color
        let base: Color
        let dark: Color

        init(_ light: UInt32, _ base: UInt32, _ dark: UInt32) {
            self.light = Color(hex: light)
            self.base = Color(hex: base)
            self.dark = Color(hex: dark)
        }
    }

    static let shades: [Shade] = [
        Shade(0xFF8A80, 0xE53935, 0xB71C1C), // cherry
        Shade(0xA1887F, 0x6D4C41, 0x3E2723), // chocolate
        Shade(0xFFF9C4, 0xFFE082, 0xF9A825), // vanilla
        Shade(0xA5D6A7, 0x66BB6A, 0x388E3C), // matcha
        Shade(0x9FA8DA, 0x5C6BC0, 0x3949AB), // blueberry
        Shade(0xF8BBD0, 0xF06292, 0xD81B60), // strawberry
        Shade(0xFFCC80, 0xFB8C00, 0xE65100), // orange
        Shade(0xCE93D8, 0xAB47BC, 0x7B1FA2)  // grape
    ]

    static func shade(for flavor: Int) -> Shade {
        shades[abs(flavor) % shades.count]
    }

    static func color(for flavor: Int) -> Color {
        shade(for: flavor).base
    }
}

/// Draws a whole or partial cake as six frosted wedge paths on a ceramic plate.
struct CakeView: View {
    let slices: [Int]

    var body: some View {
        Canvas { context, size in
            let center = CGPoint(x: size.width / 2, y: size.height / 2)
            let plateRadius = min(size.width, size.height) * 0.48
            let cakeRadius = plateRadius * 0.86

            // Soft contact shadow under the plate.
            var shadowContext = context
            shadowContext.addFilter(.blur(radius: plateRadius * 0.09))
            var shadow = Path()
            shadow.addEllipse(in: CGRect(x: center.x - plateRadius * 0.94,
                                         y: center.y - plateRadius * 0.82 + plateRadius * 0.10,
                                         width: plateRadius * 1.88,
                                         height: plateRadius * 1.88))
            shadowContext.fill(shadow, with: .color(Color(hex: 0x6D4C41).opacity(0.30)))

            // Ceramic plate: radial falloff plus a rim ring.
            var plate = Path()
            plate.addEllipse(in: CGRect(x: center.x - plateRadius, y: center.y - plateRadius,
                                        width: plateRadius * 2, height: plateRadius * 2))
            context.fill(plate, with: .radialGradient(
                Gradient(colors: [FlavorPalette.plateLight, FlavorPalette.plateDark]),
                center: CGPoint(x: center.x - plateRadius * 0.25, y: center.y - plateRadius * 0.30),
                startRadius: plateRadius * 0.1,
                endRadius: plateRadius * 1.05))
            context.stroke(plate, with: .color(FlavorPalette.ink.opacity(0.14)),
                           style: StrokeStyle(lineWidth: 1))

            var well = Path()
            well.addEllipse(in: CGRect(x: center.x - cakeRadius, y: center.y - cakeRadius,
                                       width: cakeRadius * 2, height: cakeRadius * 2))
            context.stroke(well, with: .color(FlavorPalette.ink.opacity(0.10)),
                           style: StrokeStyle(lineWidth: 1.2))

            guard !slices.isEmpty else { return }

            for (index, flavor) in slices.prefix(Rules.slicesPerCake).enumerated() {
                let shade = FlavorPalette.shade(for: flavor)
                let path = CakeView.wedgePath(index: index, center: center, radius: cakeRadius)
                context.fill(path, with: .radialGradient(
                    Gradient(colors: [shade.light, shade.base, shade.dark]),
                    center: center,
                    startRadius: cakeRadius * 0.12,
                    endRadius: cakeRadius * 1.02))
                context.stroke(path, with: .color(shade.dark.opacity(0.55)),
                               style: StrokeStyle(lineWidth: 1))
            }

            // Sugared gloss sweeping across the top-left of the cake.
            var glossContext = context
            glossContext.clip(to: well)
            var gloss = Path()
            gloss.addEllipse(in: CGRect(x: center.x - cakeRadius * 1.1,
                                        y: center.y - cakeRadius * 1.35,
                                        width: cakeRadius * 1.5,
                                        height: cakeRadius * 1.1))
            glossContext.fill(gloss, with: .linearGradient(
                Gradient(colors: [Color.white.opacity(0.38), Color.white.opacity(0.0)]),
                startPoint: CGPoint(x: center.x - cakeRadius * 0.6, y: center.y - cakeRadius),
                endPoint: CGPoint(x: center.x + cakeRadius * 0.2, y: center.y - cakeRadius * 0.1)))

            // Caramelized crust rim around the cake.
            context.stroke(well, with: .color(FlavorPalette.ink.opacity(0.22)),
                           style: StrokeStyle(lineWidth: max(1.4, cakeRadius * 0.05)))

            // Cream dollop in the middle.
            let dollopRadius = max(2.5, cakeRadius * 0.13)
            var dollop = Path()
            dollop.addEllipse(in: CGRect(x: center.x - dollopRadius,
                                         y: center.y - dollopRadius,
                                         width: dollopRadius * 2,
                                         height: dollopRadius * 2))
            context.fill(dollop, with: .radialGradient(
                Gradient(colors: [Color.white, Color(hex: 0xF3E5C8)]),
                center: CGPoint(x: center.x - dollopRadius * 0.3, y: center.y - dollopRadius * 0.35),
                startRadius: 0.5,
                endRadius: dollopRadius * 1.3))
            context.stroke(dollop, with: .color(FlavorPalette.ink.opacity(0.22)),
                           style: StrokeStyle(lineWidth: 0.7))
        }
        .accessibilityLabel(Text(slices.isEmpty ? "Empty plate" : "Cake with \(slices.count) slices"))
    }

    static func wedgePath(index: Int, center: CGPoint, radius: CGFloat) -> Path {
        var path = Path()
        path.move(to: center)
        path.addArc(center: center, radius: radius,
                    startAngle: .degrees(Double(index * 60) - 90),
                    endAngle: .degrees(Double((index + 1) * 60) - 90),
                    clockwise: false)
        path.closeSubpath()
        return path
    }
}

/// A single wedge used by the merge-flight animation.
struct WedgeView: View {
    let flavor: Int

    var body: some View {
        Canvas { context, size in
            let center = CGPoint(x: size.width / 2, y: size.height / 2)
            let radius = min(size.width, size.height) * 0.46
            let shade = FlavorPalette.shade(for: flavor)
            let path = CakeView.wedgePath(index: 0, center: center, radius: radius)
            context.fill(path, with: .radialGradient(
                Gradient(colors: [shade.light, shade.base, shade.dark]),
                center: center,
                startRadius: radius * 0.12,
                endRadius: radius))
            context.stroke(path, with: .color(shade.dark.opacity(0.55)), lineWidth: 0.8)
        }
    }
}
