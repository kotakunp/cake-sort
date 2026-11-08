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
    static let background = Color(hex: 0xFFF8EC)
    static let ink = Color(hex: 0x5D4037)
    static let accent = Color(hex: 0xFB8C00)
    static let plate = Color(hex: 0xFFFCF5)

    static let colors: [Color] = [
        Color(hex: 0xE53935), // red
        Color(hex: 0x6D4C41), // chocolate
        Color(hex: 0xFFF59D), // vanilla
        Color(hex: 0x66BB6A), // matcha
        Color(hex: 0x5C6BC0), // blueberry
        Color(hex: 0xF48FB1), // strawberry
        Color(hex: 0xFB8C00), // orange
        Color(hex: 0xAB47BC)  // grape
    ]

    static func color(for flavor: Int) -> Color {
        colors[abs(flavor) % colors.count]
    }
}

/// Draws a whole or partial cake as six procedural, sixty-degree wedge paths.
struct CakeView: View {
    let slices: [Int]

    var body: some View {
        Canvas { context, size in
            let center = CGPoint(x: size.width / 2, y: size.height / 2)
            let radius = min(size.width, size.height) * 0.46

            if slices.isEmpty {
                var plate = Path()
                plate.addEllipse(in: CGRect(x: center.x - radius, y: center.y - radius,
                                            width: radius * 2, height: radius * 2))
                context.fill(plate, with: .color(FlavorPalette.plate))
                context.stroke(plate, with: .color(FlavorPalette.ink.opacity(0.16)),
                               style: StrokeStyle(lineWidth: 1.2))
                return
            }

            for (index, flavor) in slices.prefix(Rules.slicesPerCake).enumerated() {
                let path = CakeView.wedgePath(index: index, center: center, radius: radius)
                context.fill(path, with: .color(FlavorPalette.color(for: flavor)))
                context.stroke(path, with: .color(FlavorPalette.ink.opacity(0.38)),
                               style: StrokeStyle(lineWidth: 0.8))
            }

            let dollopRadius = max(2.5, radius * 0.12)
            var dollop = Path()
            dollop.addEllipse(in: CGRect(x: center.x - dollopRadius,
                                         y: center.y - dollopRadius,
                                         width: dollopRadius * 2,
                                         height: dollopRadius * 2))
            context.fill(dollop, with: .color(Color.white.opacity(0.9)))
            context.stroke(dollop, with: .color(FlavorPalette.ink.opacity(0.25)),
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
            let path = CakeView.wedgePath(index: 0, center: center,
                                          radius: min(size.width, size.height) * 0.46)
            context.fill(path, with: .color(FlavorPalette.color(for: flavor)))
            context.stroke(path, with: .color(FlavorPalette.ink.opacity(0.4)), lineWidth: 0.8)
        }
    }
}
