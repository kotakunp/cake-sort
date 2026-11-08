import SwiftUI

struct WinOverlayView: View {
    @ObservedObject var viewModel: GameViewModel

    var body: some View {
        GameOverlay(scrim: Color.black.opacity(0.42)) {
            Text("Level Complete!")
                .font(.largeTitle.weight(.heavy))
            Text("You served \(viewModel.cakesServed) cakes and scored \(viewModel.score).")
                .multilineTextAlignment(.center)
            Text("High score: \(viewModel.highScore)")
                .font(.headline)
                .foregroundColor(FlavorPalette.accent)
            Button("Next Level") {
                viewModel.nextLevel()
            }
            .buttonStyle(BakeryButtonStyle())
        }
    }
}

struct LoseOverlayView: View {
    @ObservedObject var viewModel: GameViewModel

    var body: some View {
        GameOverlay(scrim: Color.black.opacity(0.42)) {
            Text("No more room!")
                .font(.largeTitle.weight(.heavy))
            Text("Clear some space with the hammer and try a new strategy.")
                .multilineTextAlignment(.center)
            Button("Try Again") {
                viewModel.retry()
            }
            .buttonStyle(BakeryButtonStyle())
        }
    }
}

private struct GameOverlay<Content: View>: View {
    let scrim: Color
    let content: Content

    init(scrim: Color, @ViewBuilder content: () -> Content) {
        self.scrim = scrim
        self.content = content()
    }

    var body: some View {
        ZStack {
            scrim.ignoresSafeArea()
            VStack(spacing: 18) {
                content
            }
            .foregroundColor(FlavorPalette.ink)
            .padding(28)
            .frame(maxWidth: 340)
            .background(
                RoundedRectangle(cornerRadius: 26)
                    .fill(FlavorPalette.background)
                    .shadow(color: .black.opacity(0.22), radius: 18, x: 0, y: 8)
            )
            .padding(24)
            .transition(.scale.combined(with: .opacity))
        }
    }
}

private struct BakeryButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.headline)
            .foregroundColor(.white)
            .padding(.horizontal, 28)
            .padding(.vertical, 12)
            .background(Capsule().fill(FlavorPalette.accent))
            .scaleEffect(configuration.isPressed ? 0.95 : 1)
    }
}
