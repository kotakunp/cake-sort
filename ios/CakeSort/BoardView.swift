import SwiftUI

struct PlateFramePreferenceKey: PreferenceKey {
    static var defaultValue: [Int: CGRect] = [:]

    static func reduce(value: inout [Int: CGRect], nextValue: () -> [Int: CGRect]) {
        value.merge(nextValue()) { current, _ in current }
    }
}

struct BoardView: View {
    @ObservedObject var viewModel: GameViewModel

    var body: some View {
        GeometryReader { geometry in
            let side = min(geometry.size.width, geometry.size.height)
            let cell = side / CGFloat(Rules.boardCols)
            let origin = CGPoint(x: (geometry.size.width - side) / 2,
                                 y: (geometry.size.height - side) / 2)

            ZStack {
                RoundedRectangle(cornerRadius: 22)
                    .fill(LinearGradient(colors: [FlavorPalette.trayTop, FlavorPalette.trayBottom],
                                         startPoint: .top, endPoint: .bottom))
                    .frame(width: side + 10, height: side + 10)
                    .overlay(
                        RoundedRectangle(cornerRadius: 22)
                            .stroke(FlavorPalette.trayEdge.opacity(0.8), lineWidth: 2)
                            .frame(width: side + 10, height: side + 10)
                    )
                    .overlay(
                        RoundedRectangle(cornerRadius: 20)
                            .stroke(Color.white.opacity(0.35), lineWidth: 1)
                            .frame(width: side + 4, height: side + 4)
                    )
                    .shadow(color: Color(hex: 0x6D4C41).opacity(0.35), radius: 10, x: 0, y: 6)
                    .position(x: geometry.size.width / 2, y: geometry.size.height / 2)

                ForEach(0..<(Rules.boardCols * Rules.boardRows), id: \.self) { index in
                    plateView(index: index, cell: cell)
                        .position(position(for: index, cell: cell, origin: origin))
                }

                if let flight = viewModel.mergeFlight {
                    FlyingSlicesView(flight: flight, cell: cell,
                                     start: position(for: flight.from, cell: cell, origin: origin),
                                     end: position(for: flight.to, cell: cell, origin: origin))
                }

                ForEach(viewModel.comboPopups) { popup in
                    Text(popup.text)
                        .font(.system(size: 16, weight: .heavy, design: .rounded))
                        .foregroundColor(FlavorPalette.accent)
                        .padding(.horizontal, 8)
                        .padding(.vertical, 4)
                        .background(Capsule().fill(Color.white.opacity(0.92)))
                        .shadow(radius: 2)
                        .position(popupPosition(for: popup.plate, cell: cell, origin: origin))
                        .transition(.scale.combined(with: .opacity))
                        .allowsHitTesting(false)
                }
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(Text("Five by five cake board"))
    }

    private func plateView(index: Int, cell: CGFloat) -> some View {
        let isServing = viewModel.serveAnimation?.plate == index
        return ZStack {
            CakeView(slices: viewModel.plates[index].slices)
                .padding(2)
            Circle()
                .stroke(viewModel.highlightedPlate == index ? FlavorPalette.accent : Color.clear,
                        lineWidth: 3)
                .padding(1)
        }
        .frame(width: cell - 4, height: cell - 4)
        .scaleEffect(isServing ? 1.16 : 1)
        .opacity(isServing ? 0 : 1)
        .offset(x: viewModel.shakingPlate == index ? 6 : 0)
        .contentShape(Rectangle())
        .onTapGesture {
            viewModel.tapPlate(index)
        }
        .background(
            GeometryReader { proxy in
                Color.clear.preference(
                    key: PlateFramePreferenceKey.self,
                    value: [index: proxy.frame(in: .named(GameViewModel.gameCoordinateSpace))]
                )
            }
        )
    }

    private func position(for index: Int, cell: CGFloat, origin: CGPoint) -> CGPoint {
        CGPoint(x: origin.x + (CGFloat(index % Rules.boardCols) + 0.5) * cell,
                y: origin.y + (CGFloat(index / Rules.boardCols) + 0.5) * cell)
    }

    private func popupPosition(for index: Int, cell: CGFloat, origin: CGPoint) -> CGPoint {
        var point = position(for: index, cell: cell, origin: origin)
        point.y -= cell * 0.34
        return point
    }
}

private struct FlyingSlicesView: View {
    let flight: MergeFlight
    let cell: CGFloat
    let start: CGPoint
    let end: CGPoint
    @State private var progress: CGFloat = 0

    var body: some View {
        ForEach(0..<flight.count, id: \.self) { index in
            let spread = CGFloat(index - (flight.count - 1) / 2) * 5
            WedgeView(flavor: flight.flavor)
                .frame(width: cell * 0.34, height: cell * 0.34)
                .rotationEffect(.degrees(Double(index) * 35))
                .position(x: start.x + (end.x - start.x) * progress + spread,
                          y: start.y + (end.y - start.y) * progress - spread)
                .shadow(radius: 2)
        }
        .onAppear {
            withAnimation(.easeInOut(duration: 0.2)) {
                progress = 1
            }
        }
    }
}
