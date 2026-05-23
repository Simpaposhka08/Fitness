from server import load_samples, train_model


def main() -> None:
    samples = load_samples()
    result = train_model(samples)
    print(result)


if __name__ == "__main__":
    main()
