import SwiftUI

@main
struct CakeSortApp: App {
    @StateObject private var viewModel = GameViewModel()

    var body: some Scene {
        WindowGroup {
            GameRootView(viewModel: viewModel)
        }
    }
}

/// Scattered sugar sprinkles behind the whole screen, deterministic per launch.
struct SprinkleFieldView: View {
    private struct Sprinkle: Identifiable {
        let id: Int
        let position: CGPoint
        let angle: Double
        let color: Color
    }

    private let sprinkles: [Sprinkle] = {
        var rng = SplitMix64(seed: 0xCAFE)
        return (0..<42).map { index in
            Sprinkle(id: index,
                     position: CGPoint(x: rng.nextDouble(), y: rng.nextDouble()),
                     angle: rng.nextDouble() * 360,
                     color: FlavorPalette.color(for: index).opacity(0.16))
        }
    }()

    var body: some View {
        GeometryReader { geometry in
            ForEach(sprinkles) { sprinkle in
                Capsule()
                    .fill(sprinkle.color)
                    .frame(width: 12, height: 4)
                    .rotationEffect(.degrees(sprinkle.angle))
                    .position(x: sprinkle.position.x * geometry.size.width,
                              y: sprinkle.position.y * geometry.size.height)
            }
        }
    }
}

struct GameRootView: View {
    @ObservedObject var viewModel: GameViewModel

    var body: some View {
        ZStack {
            LinearGradient(colors: [FlavorPalette.backgroundTop, FlavorPalette.backgroundBottom],
                           startPoint: .top, endPoint: .bottom)
                .ignoresSafeArea()
            SprinkleFieldView()
                .ignoresSafeArea()
                .allowsHitTesting(false)

            VStack(spacing: 10) {
                HUDView(viewModel: viewModel)
                BoardView(viewModel: viewModel)
                    .aspectRatio(1, contentMode: .fit)
                    .padding(.horizontal, 12)
                BoosterBarView(viewModel: viewModel)
                SlotBarView(viewModel: viewModel)
                    .padding(.horizontal, 12)
            }
            .padding(.vertical, 8)
            .disabled(viewModel.state != .playing)

            if let slot = viewModel.draggingSlot,
               let cake = viewModel.slots[slot] {
                CakeView(slices: cake.slices)
                    .frame(width: 82, height: 82)
                    .shadow(color: .black.opacity(0.22), radius: 8, x: 0, y: 5)
                    .position(viewModel.dragLocation)
                    .allowsHitTesting(false)
            }

            if viewModel.state == .won {
                WinOverlayView(viewModel: viewModel)
            } else if viewModel.state == .lost {
                LoseOverlayView(viewModel: viewModel)
            }
        }
        .coordinateSpace(name: GameViewModel.gameCoordinateSpace)
        .onPreferenceChange(PlateFramePreferenceKey.self) { frames in
            viewModel.plateFrames = frames
        }
    }
}
