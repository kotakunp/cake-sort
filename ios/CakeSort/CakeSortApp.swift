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

struct GameRootView: View {
    @ObservedObject var viewModel: GameViewModel

    var body: some View {
        ZStack {
            FlavorPalette.background
                .ignoresSafeArea()

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
